// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    /// <summary>
    /// TFTP packet types as specified by RFC 1350
    /// </summary>
    public enum ETftpPacketType
    {
        /// <summary>
        /// Read a file from the server
        /// </summary>
        ReadRequest = 1,

        /// <summary>
        /// Write a file to the server
        /// </summary>
        WriteRequest = 2,

        /// <summary>
        /// A packet containing a data payload
        /// </summary>
        Data = 3,

        /// <summary>
        /// A TFTP acknowledgement for a data packet
        /// </summary>
        Acknowledgement = 4,

        /// <summary>
        /// A TFTP error packet
        /// </summary>
        Error = 5
    }
}
