// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    /// <summary>
    /// Information parsed from a TFTP request packet
    /// </summary>
    internal class TftpRequest
    {
        /// <summary>
        /// The filename in the request
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// The mode specified in the request
        /// </summary>
        public string Mode { get; set; }
    }
}
