// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    /// <summary>
    /// TFTP Error types as transmitted in TFTP error packets. Specified in RFC1350
    /// </summary>
    public enum ETftpErrorType
    {
        /// <summary>
        /// Not defined, see error message (if any).
        /// </summary>
        NotDefined = 0,

        /// <summary>
        /// File not found.
        /// </summary>
        FileNotFound = 1,

        /// <summary>
        /// Access violation.
        /// </summary>
        AccessViolation = 2,

        /// <summary>
        /// Disk full or allocation exceeded.
        /// </summary>
        DiskFullOrAllocationExceeded = 3,

        /// <summary>
        /// Illegal TFTP operation.
        /// </summary>
        IllegalOperation = 4,

        /// <summary>
        /// Unknown transfer ID.
        /// </summary>
        UnknownTransferId = 5,

        /// <summary>
        /// File already exists.
        /// </summary>
        FileAlreadyExists = 6,

        /// <summary>
        /// No such user.
        /// </summary>
        NoSuchUser = 7
    }
}
