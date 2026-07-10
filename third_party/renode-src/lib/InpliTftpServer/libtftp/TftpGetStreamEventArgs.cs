// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

namespace libtftp
{
    using System;
    using System.IO;
    using System.Net;

    /// <summary>
    /// Callback event arguments required for processing a TFTP read request
    /// </summary>
    public class TftpGetStreamEventArgs : EventArgs
    {
        /// <summary>
        /// Session Id
        /// </summary>
        public Guid Id { get; set; }
        
        /// <summary>
        /// The remote host requesting the read
        /// </summary>
        public IPEndPoint RemoteHost { get; set; }

        /// <summary>
        /// The filename requested by the remote host
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// The stream to transfer to the remote host. This is provided by the callback.
        /// </summary>
        public Stream Result { get; set; }
    }
}