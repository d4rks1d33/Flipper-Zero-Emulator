// *******************************************************************
//
//  Copyright (c) 2013-2022, Antmicro Ltd <antmicro.com>
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
using System.Collections.Generic;
using System.Threading;

using AntShell.Encoding;

namespace AntShell.Terminal
{
    public class IOProvider : IDisposable
    {
        public IOProvider()
        {
            localBuffer = new Queue<byte>();
            encoding = System.Text.Encoding.GetEncoding("UTF-8", System.Text.EncoderFallback.ReplacementFallback, new CustomDecoderFallback());
        }

        public void Dispose()
        {
            var disposable = backend as IDisposable;
            if(disposable != null)
            {
                disposable.Dispose();
            }
        }

        public void CancelGet()
        {
            var b = Backend as IPassiveIOSource;
            if(b != null)
            {
                b.CancelRead();
            }
        }

        public char? GetNextChar()
        {
            while(true)
            {
                var firstByte = GetNextByte();
                var numberOfBytesToRead = 0;
                if(firstByte < 0)
                {
                    return null;
                }
                if(firstByte < 0x80)
                {
                    return (char)firstByte;
                }
                else if((firstByte & 0xE0) == 0xC0)
                {
                    // two bytes
                    numberOfBytesToRead = 1;
                }
                else if((firstByte & 0xF0) == 0xE0)
                {
                    // three bytes
                    numberOfBytesToRead = 2;
                }
                else
                {
                    // four bytes
                    numberOfBytesToRead = 3;
                }

                var bytes = new byte[numberOfBytesToRead + 1];
                bytes[0] = (byte)firstByte;
                for(int i = 1; i < bytes.Length; i++)
                {
                    var nextByte = GetNextByte();
                    if(nextByte < 0)
                    {
                        return null;
                    }
                    bytes[i] = (byte)nextByte;
                }

                var decodedChar = encoding.GetChars(bytes)[0];
                if(!((CustomDecoderFallback)encoding.DecoderFallback).IsError)
                {
                    return decodedChar;
                }
            }
        }

        public int GetNextByte()
        {
            if(isInActiveMode)
            {
                throw new InvalidOperationException("Cannot use 'GetNextByte' method when a callback is attached to 'ByteRead' event.");
            }

            if(localBuffer.Count > 0)
            {
                return localBuffer.Dequeue();
            }

            return ((IPassiveIOSource)Backend).Read();
        }


        public void Write(char c)
        {
            foreach(var b in encoding.GetBytes(new[] { c }))
            {
                Write(b);
            }
        }

        public void Write(byte b)
        {
            Backend.Write(b);
        }

        public void ClearPeeked()
        {
            localBuffer.Clear();
        }

        public void Flush()
        {
            ClearPeeked();
            Backend.Flush();
        }

        public void Pause()
        {
            var activeBackend = backend as IActiveIOSource;
            if(activeBackend != null)
            {
                activeBackend.Pause();
            }
        }

        public void Resume()
        {
            var activeBackend = backend as IActiveIOSource;
            if(activeBackend != null)
            {
                activeBackend.Resume();
            }
        }

        public IIOSource Backend
        {
            get
            {
                if(backend == null)
                {
                    throw new NotSupportedException("The IOProvider cannot be used before initialization is finished.");
                }
                return backend;
            }

            set
            {
                if(value == null)
                {
                    throw new ArgumentNullException();
                }
                if(backend != null)
                {
                    throw new NotSupportedException("The IOProvider has already been initialized.");
                }
                backend = value;
                SwitchToPassive();
            }
        }

        public bool HasNewInput
        {
            get
            {
                return isInActiveMode
                    ? Interlocked.Exchange(ref inputIsWaiting, 0) == 1
                    : (Backend as IPassiveIOSource).TryPeek(out var _);
            }
        }

        public bool IsBackendSet => backend != null;

        public event Action<int> ByteRead
        {
            add
            {
                SwitchToActive();
                foreach(var b in localBuffer)
                {
                    value(b);
                }
                localBuffer.Clear();
                ((IActiveIOSource)Backend).ByteRead += value;
            }

            remove
            {
                ((IActiveIOSource)Backend).ByteRead -= value;
                if(((IActiveIOSource)Backend).IsAnythingAttached)
                {
                    SwitchToPassive();
                }
            }
        }

        internal void Inject(char c)
        {
            if(isInActiveMode)
            {
                throw new InvalidOperationException("Cannot use 'Inject' method when a callback is attached to 'ByteRead' event.");
            }

            foreach(var b in encoding.GetBytes(new[] { c }))
            {
                localBuffer.Enqueue(b);
            }
        }

        private void SwitchToActive()
        {
            var passiveBackend = backend as IPassiveIOSource;
            if(passiveBackend != null)
            {
                var activeBackend = new PAIOSourceConverter(passiveBackend);
                Interlocked.Exchange(ref inputIsWaiting, 0);
                activeBackend.ByteRead += HandleNewData;
                backend = activeBackend;
                isInActiveMode = true;
            }
        }

        private void SwitchToPassive()
        {
            var activeBackend = backend as IActiveIOSource;
            if(activeBackend != null)
            {
                activeBackend.ByteRead -= HandleNewData;
                backend = new APIOSourceConverter(activeBackend);
                isInActiveMode = false;
            }
        }

        private void HandleNewData(int b)
        {
            Interlocked.Exchange(ref inputIsWaiting, 1);
        }

        private IIOSource backend;
        private bool isInActiveMode;
        // it's an integer, so that we can use Interlocked.Exchange method
        private int inputIsWaiting;

        private readonly Queue<byte> localBuffer;
        private readonly System.Text.Encoding encoding;
    }
}
