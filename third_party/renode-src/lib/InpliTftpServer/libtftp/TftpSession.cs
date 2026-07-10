// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// An instance of an individual TFTP session
    /// </summary>
    internal class TftpSession
    {
        /// <summary>
        /// An auto generated session id
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The parent/owner TftpServer object
        /// </summary>
        public TftpServer Parent { get; private set; }

        /// <summary>
        /// The remote host which initiated the session
        /// </summary>
        public IPEndPoint RemoteHost { get; private set; }

        /// <summary>
        /// The operation requested by the remote host
        /// </summary>
        public ETftpOperationType Operation { get; private set; } = ETftpOperationType.Unspecified;

        /// <summary>
        /// The filename requested to send or receive
        /// </summary>
        public string Filename { get; private set; }

        /// <summary>
        /// When was the transfer request received
        /// </summary>
        public DateTimeOffset TransferRequestInitiated { get; private set; }

        /// <summary>
        /// When was the last packet received from the remote host
        /// </summary>
        public DateTimeOffset IdleSince { get; private set; }

        /// <summary>
        /// The maximum retransmit count
        /// </summary>
        public int MaximumRetries { get; private set; } = 5;

        internal Stream TransferStream { get; set; }

        /// <summary>
        /// The current block number
        /// </summary>
        private long CurrentBlock { get; set; } = 0;

        /// <summary>
        /// The time when the last status debug message was logged
        /// </summary>
        private DateTimeOffset LastMessageTime { get; set; } = DateTimeOffset.MinValue;

        /// <summary>
        /// The number of bytes received so far
        /// </summary>
        private long BytesReceived { get; set; }

        private byte[] TransmitBuffer = new byte[516];

        private int TransmitBufferLength = 0;

        private int BlockTransmitCount = 0;

        public long Position { get; private set; } = 0;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="parent">The owner of this session</param>
        /// <param name="remoteHost">The remote host which this session correlates to</param>
        internal TftpSession(TftpServer parent, IPEndPoint remoteHost)
        {
            Parent = parent;
            RemoteHost = remoteHost;
            IdleSince = DateTimeOffset.Now;
            Id = Guid.NewGuid();
        }

        /// <summary>
        /// Syslog an error
        /// </summary>
        /// <param name="message">The message to log</param>
        private void LogError(string message)
        {
            Parent.LogError(RemoteHost.ToString() + ": " + message);
        }

        /// <summary>
        /// Syslog an informative message
        /// </summary>
        /// <param name="message">The message to log</param>
        private void LogInfo(string message)
        {
            Parent.LogInfo(RemoteHost.ToString() + ": " + message);
        }

        /// <summary>
        /// Syslog a debug message
        /// </summary>
        /// <param name="message">The message to log</param>
        private void LogDebug(string message)
        {
            Parent.LogDebug(RemoteHost.ToString() + ": " + message);
        }

        /// <summary>
        /// Receive and process a packet
        /// </summary>
        /// <param name="messageData">The buffer received, it must be trimmed</param>
        internal async Task OnReceiveAsync(byte[] messageData)
        {
            IdleSince = DateTimeOffset.Now;

            // No reason to throw if the message is empty
            if (messageData.Length < 2)
                return;

            var messageType = messageData.Get16BE(0);
            switch((ETftpPacketType)messageType)
            {
                case ETftpPacketType.WriteRequest:
                    OnWriteRequest(messageData);
                    break;

                case ETftpPacketType.ReadRequest:
                    await OnReadRequestAsync(messageData);
                    break;

                case ETftpPacketType.Data:
                    await OnDataReceivedAsync(messageData);
                    break;

                case ETftpPacketType.Acknowledgement:
                    await OnAcknowledgeAsync(messageData);
                    break;

                case ETftpPacketType.Error:
                    OnError(messageData);
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        private void OnError(byte[] messageData)
        {
            if(messageData.Length >= 5)
            {
                var errorCode = (ETftpErrorType)messageData.Get16BE(2);
                var start = 4;
                var index = start;
                while (index < messageData.Length && messageData[index] != 0)
                    index++;

                if(messageData[index] == 0)
                {
                    var message = Encoding.UTF8.GetString(messageData, start, index - start);
                    LogError("Client error: " + errorCode.ToString() + ": " + message);
                    Parent.UnregisterSession(this, message);
                }
            }
            else
            {
                Parent.UnregisterSession(this, "unknown");
            }
        }

        private async Task OnAcknowledgeAsync(byte[] messageData)
        {
            if (messageData.Length < 4)
            {
                await RetransmitAsync();
                return;
            }

            if(TransmitBufferLength < 516)
            {
                await Parent.TransferCompleteAsync(this);
                return;
            }

            TransmitBufferLength = 0;
            await RetransmitAsync();
        }

        /// <summary>
        /// Used to transmit or retransmit the current buffer
        /// </summary>
        internal async Task RetransmitAsync()
        {
            if(TransmitBufferLength == 0)
            {
                int bytesRead = await TransferStream.ReadAsync(TransmitBuffer, 4, 512);
                Position += bytesRead;

                TransmitBufferLength = bytesRead + 4;
                CurrentBlock++;
                BlockTransmitCount = 0;
                TransmitBuffer.Write16BE(0, (int)ETftpPacketType.Data);
                TransmitBuffer.Write16BE(2, (int)(CurrentBlock & 0xFFFF));
            }
            else
            {
                BlockTransmitCount++;
                if(BlockTransmitCount > MaximumRetries)
                {
                    TransmitError(ETftpErrorType.NotDefined, "Maximum rety count exceeded");
                    LogError("Maximum retry count exceeded");
                    Parent.UnregisterSession(this);

                    return;
                }
            }

            Parent.Transmit(RemoteHost, TransmitBuffer, TransmitBufferLength);
        }

        private async Task OnReadRequestAsync(byte[] messageData)
        {
            LogDebug("Received read request");

            var request = ProcessRequestHeader(messageData);
            if (request == null)
                return;

            if (Operation == ETftpOperationType.WriteOperation)
            {
                TransmitError(ETftpErrorType.IllegalOperation, "Already processing WriteRequest");
            }
            else if (TransferStream != null)
            {
                TransmitError(ETftpErrorType.IllegalOperation, "Read request already in progress");
            }
            else if (!string.IsNullOrEmpty(Filename) && Filename != request.Filename)
            {
                TransmitError(ETftpErrorType.IllegalOperation, "Read request conflicts with previous read request");
            }
            else
            {
                TransferStream = await Parent.GetReadStreamAsync(Id, RemoteHost, request.Filename);
                if(TransferStream == null)
                {
                    TransmitError(ETftpErrorType.FileNotFound, "File not found");
                    return;
                }

                Operation = ETftpOperationType.ReadOperation;
                Filename = request.Filename;
                TransferRequestInitiated = IdleSince;
                await RetransmitAsync();
            }
        }

        private async Task OnDataReceivedAsync(byte[] messageData)
        {
            if (messageData.Length < 4)
            {
                LogDebug("Packet ended prematurely on receive");
                TransmitError(ETftpErrorType.IllegalOperation, "Packet ended prematurely");
                Parent.UnregisterSession(this);

                return;
            }

            var blockNumber = messageData.Get16BE(2);

            if(blockNumber != ((CurrentBlock + 1) & 0xFFFF))
            {
                LogDebug("Block received out of sequence");
                TransmitError(ETftpErrorType.IllegalOperation, "Block received out of sequence");
                Parent.UnregisterSession(this);
            }

            BytesReceived += messageData.Length - 4;

            CurrentBlock++;
            TransmitAck(blockNumber);

            if(TransferStream == null)
            {
                if(CurrentBlock != 1)
                {
                    LogDebug("ReceiveStream not created yet but not on first packet. Ending transfer");
                    TransmitError(ETftpErrorType.NotDefined, "Server error");
                    Parent.UnregisterSession(this);
                }

                TransferStream = new MemoryStream();
            }

            TransferStream.Write(messageData, 4, messageData.Length - 4);

            if (messageData.Length != 516)
            {
                LogDebug("Last block received, transfer complete");
                await Parent.TransferCompleteAsync(this);
            }
            else
            {
                if (IdleSince.Subtract(LastMessageTime) > TimeSpan.FromSeconds(1))
                {
                    LogDebug("Received " + BytesReceived.ToString() + " bytes so far");
                    LastMessageTime = IdleSince;
                }
            }
        }

        private TftpRequest ProcessRequestHeader(byte [] messageData)
        {
            var index = 2;

            var startOfFileName = index;
            while (index < messageData.Length && messageData[index] != 0)
                index++;

            if (index >= messageData.Length || messageData[index] != 0)
            {
                LogDebug("Message ends prematurely while reading filename");
                TransmitError(ETftpErrorType.IllegalOperation, "Filename not specified");

                return null;
            }

            var fileName = Encoding.UTF8.GetString(messageData.Skip(startOfFileName).Take(index - startOfFileName).ToArray());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                LogDebug("Message contains null or empty filename");
                TransmitError(ETftpErrorType.IllegalOperation, "Filename not specified");

                return null;
            }

            LogDebug("Request for filename: " + fileName);

            index++;
            var startOfModeString = index;
            while (index < messageData.Length && messageData[index] != 0)
                index++;

            if (index >= messageData.Length || messageData[index] != 0)
            {
                LogDebug("Message ends prematurely while reading mode");
                TransmitError(ETftpErrorType.IllegalOperation, "Transfer mode not specified");

                return null;
            }

            var mode = Encoding.UTF8.GetString(messageData.Skip(startOfModeString).Take(index - startOfModeString).ToArray());
            if (string.IsNullOrWhiteSpace(mode))
            {
                LogDebug("Message contains null or empty mode");
                TransmitError(ETftpErrorType.IllegalOperation, "Transfer mode not specified");

                return null;
            }

            LogDebug("Request mode: " + mode);

            if (mode != "octet")
            {
                LogDebug("Unhandled TFTP mode " + mode);
                TransmitError(ETftpErrorType.IllegalOperation, "Unhandled TFTP transfer mode");

                return null;
            }

            return new TftpRequest
            {
                Filename = fileName,
                Mode = mode
            };
        }

        private void OnWriteRequest(byte[] messageData)
        {
            LogDebug("Received write request");

            var request = ProcessRequestHeader(messageData);
            if (request == null)
                return;

            if (Operation == ETftpOperationType.ReadOperation)
            {
                TransmitError(ETftpErrorType.IllegalOperation, "Already processing ReadRequest");
            }
            else if (TransferStream != null)
            {
                TransmitError(ETftpErrorType.IllegalOperation, "Write request already in progress");
            }
            else if(!string.IsNullOrEmpty(Filename) && Filename != request.Filename)
            {
                TransmitError(ETftpErrorType.IllegalOperation, "Write request conflicts with previous write request");
            }
            else
            {
                TransmitAck((int)(CurrentBlock & 0xFFFF));

                Operation = ETftpOperationType.WriteOperation;
                Filename = request.Filename;
                TransferRequestInitiated = IdleSince;
            }
        }

        private void TransmitAck(int blockNumber)
        {
            Parent.Transmit(
                RemoteHost,
                new byte[]
                {
                    00, (byte)ETftpPacketType.Acknowledgement,
                    (byte)((blockNumber >> 8) & 0xff),
                    (byte)(blockNumber & 0xff)
                }
            );
        }

        private void TransmitError(ETftpErrorType errorNumber, string message)
        {
            Parent.Transmit(
                RemoteHost,
                new byte[]
                {
                    00, (byte)ETftpPacketType.Error,
                    (byte)(((int)errorNumber >> 8) & 0xff),
                    (byte)((int)errorNumber & 0xff)
                }
                .Concat(Encoding.UTF8.GetBytes(message))
                .Concat(new byte[] { 0 })
                .ToArray()
            );
        }
    }
}
