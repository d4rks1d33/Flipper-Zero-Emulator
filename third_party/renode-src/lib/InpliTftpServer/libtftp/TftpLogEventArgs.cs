// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    using System;

    /// <summary>
    /// Event argments for logging events
    /// </summary>
    public class TftpLogEventArgs : EventArgs
    {
        /// <summary>
        /// The timestamp of the log message
        /// </summary>
        public DateTimeOffset TimeStamp { get; set; }
        
        /// <summary>
        /// The severity level of the message
        /// </summary>
        public ETftpLogSeverity Severity { get; set; }

        /// <summary>
        /// The message to log
        /// </summary>
        public string Message { get; set; }
    }
}
