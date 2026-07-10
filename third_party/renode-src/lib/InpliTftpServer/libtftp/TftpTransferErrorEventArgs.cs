// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    /// <summary>
    /// Event arguments for failed transfers
    /// </summary>
    public class TftpTransferErrorEventArgs : TftpTransferCompleteEventArgs
    {
        /// <summary>
        /// Description of the transfer failure.
        /// </summary>
        public string FailureReason { get; set; }
    }
}
