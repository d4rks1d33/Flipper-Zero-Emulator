// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    /// <summary>
    /// Syslog severity levels as specified by RFC5424
    /// </summary>
    public enum ETftpLogSeverity
    {
        /// <summary>
        /// Emergency: system is unusable
        /// </summary>
        Emergency = 0,

        /// <summary>
        /// Alert: action must be taken immediately
        /// </summary>
        Alert = 1,

        /// <summary>
        /// Critical: critical conditions
        /// </summary>
        Critical = 2,

        /// <summary>
        /// Error: error conditions
        /// </summary>
        Error = 3,

        /// <summary>
        /// Warning: warning conditions
        /// </summary>
        Warning = 4,

        /// <summary>
        /// Notice: normal but significant condition
        /// </summary>
        Notice = 5,

        /// <summary>
        /// Informational: informational messages
        /// </summary>
        Informational = 6,

        /// <summary>
        /// Debug: debug-level messages
        /// </summary>
        Debug = 7
    }
}
