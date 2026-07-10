// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    /// <summary>
    /// A TFTP operation type
    /// </summary>
    public enum ETftpOperationType
    {
        /// <summary>
        /// This is the state before the request type is known
        /// </summary>
        Unspecified,

        /// <summary>
        /// Processing a TFTP write request
        /// </summary>
        WriteOperation,

        /// <summary>
        /// Processing a TFTP read request
        /// </summary>
        ReadOperation
    }
}
