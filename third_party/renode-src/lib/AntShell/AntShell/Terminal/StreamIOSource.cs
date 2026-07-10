// *******************************************************************
//
//  Copyright (c) 2013-2014, Antmicro Ltd <antmicro.com>
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// *******************************************************************
using System;
using System.IO;
using System.Threading;

namespace AntShell.Terminal
{
    public class StreamIOSource : IPassiveIOSource
    {
        public void Dispose()
        {
            CancelRead();
            InputStream.Dispose();
        }

        #region ITerminalIOSource implementation

        public void Flush()
        {
            peekedByte = -1;
            InputStream.Flush();
            OutputStream.Flush();
        }

        public void Write(byte b)
        {
            OutputStream.WriteByte(b);
        }

        public bool TryPeek(out int value)
        {
            if(peekedByte != -1)
            {
                value = peekedByte;
                return true;
            }
            if(InputStream.Length > 0)
            {
                peekedByte = InputStream.ReadByte();
                value = peekedByte;
                return true;
            }
            value = -1;
            return false;
        }

        public int Read()
        {
            if(peekedByte != -1)
            {
                var result = peekedByte;
                peekedByte = -1;
                return result;
            }

            try
            {
                var asyncReadResult = InputStream.ReadAsync(buffer, 0, 1);
                asyncReadResult.Wait(cancellationToken.Token);
                return asyncReadResult.Result == 0 ? -1 : buffer[0];
            }
            catch(ObjectDisposedException)
            {
                return -1;
            }
            catch(OperationCanceledException)
            {
                return -1;
            }
            catch(AggregateException ae)
            {
                // The `cancellationToken` will not actually cancel the read so it may throw
                // an `IOException` in case the file descriptor is disposed externally but
                // if the token is cancelled we can assume that's the expected behavior
                ae.Handle(x => (x is IOException) && cancellationToken.Token.IsCancellationRequested);
                return -1;
            }
        }

        public void CancelRead()
        {
            cancellationToken.Cancel();
        }

        public void Close()
        {
            InputStream.Close();
            OutputStream.Close();
        }

        #endregion

        public StreamIOSource(Stream stream) : this(stream, stream)
        {
        }

        public StreamIOSource(Stream input, Stream output)
        {
            InputStream = input;
            OutputStream = output;

            cancellationToken = new CancellationTokenSource();
            buffer = new byte[1];
            peekedByte = -1;
        }

        public Stream InputStream { get; private set; }

        public Stream OutputStream { get; private set; }

        private CancellationTokenSource cancellationToken;
        private byte[] buffer;
        private int peekedByte;
    }
}

