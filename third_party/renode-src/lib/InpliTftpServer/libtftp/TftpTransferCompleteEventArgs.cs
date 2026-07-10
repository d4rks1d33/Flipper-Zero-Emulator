// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    using System;
    using System.IO;
    using System.Net;

    /// <summary>
    /// Event argments for completion of a file transfer
    /// </summary>
    public class TftpTransferCompleteEventArgs : EventArgs
    {
        /// <summary>
        /// Session ID
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Whether the transfer was send or receive
        /// </summary>
        public ETftpOperationType Operation { get; set; }

        /// <summary>
        /// The filename transfered
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// The time the transfer request was received
        /// </summary>
        public DateTimeOffset TransferInitiated { get; set; }

        /// <summary>
        /// The time the transfer completed
        /// </summary>
        public DateTimeOffset TransferCompleted { get; set; }

        /// <summary>
        /// The remote host from which the request was received
        /// </summary>
        public IPEndPoint RemoteHost { get; set; }

        /// <summary>
        /// For receive operations, the stream containing the transfered data
        /// </summary>
        public MemoryStream Stream { get; set; }

        /// <summary>
        /// The number of bytes that have been transferred
        /// </summary>
        public long Transferred { get; set; }
    }
}
