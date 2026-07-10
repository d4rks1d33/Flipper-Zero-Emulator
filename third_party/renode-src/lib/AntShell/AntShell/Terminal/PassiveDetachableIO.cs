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
using System.Collections.Generic;
using AntShell.Encoding;
using System;
using System.Threading;


namespace AntShell.Terminal
{
    public class DetachableIO
    {
        public DetachableIO()
        {
            locker = new object();
            localBuffer = new List<byte>();
            encoding = System.Text.Encoding.GetEncoding("UTF-8", System.Text.EncoderFallback.ReplacementFallback, new CustomDecoderFallback());
        }

        public DetachableIO(ITerminalIOSource source) : base()
        {
            Attach(source);
        }

        public void Attach(ITerminalIOSource source)
        {
            lock (locker)
            {
                this.source = source;
                Monitor.Pulse(locker);
            }
        }

        public void Detach()
        {
            lock (locker)
            {
                source = null;
            }
        }

        public Action<byte> BytePrinted;

        public void Write(char c)
        {
            foreach (var b in encoding.GetBytes(new [] { c }))
            {
                Write(b);
            }
        }

        public void Write(byte b)
        {
            lock (locker)
            {
                if (source != null)
                {
                    source.Write(b);
                }
            }

            var bp = BytePrinted;
            if (bp != null)
            {
                bp(b);
            }
        }

        public char? GetNextChar(int timeout = -1)
        {
            return InternalReadCharHandler(GetNextByte, timeout);
        }

        public char? PeekNextChar(int timeout = -1)
        {
            return InternalReadCharHandler(PeekNextByte, timeout);
        }

        public int GetNextByte(int timeout = -1)
        {
            int result;
            if (localBuffer.Count > 0)
            {
                result = localBuffer[0];
                localBuffer.RemoveAt(0);
                return result;
            }

            return InternalRead(timeout);
        }

        public int PeekNextByte(int timeout = -1)
        {
            int result = InternalRead(timeout);
            if (result >= 0)
            {
                localBuffer.Add((byte)result);
            }   
            return result;
        }

        public void ClearPeeked()
        {
            localBuffer.Clear();
        }

        public void Flush()
        {
            ClearPeeked();
            source.Flush();
        }

        private int InternalRead(int timeout)
        {
            lock (locker)
            {
                if (source == null)
                {
                    Monitor.Wait(locker);
                }

                return source.Read(timeout);
            }
        }

        private char? InternalReadCharHandler(Func<int, int> provider, int timeout)
        {
            var bytes = new byte[2];
            int res;

            for (int i = 0; i < 2; i++) 
            {
                res = provider(timeout);
                if (res < 0)
                {
                    return null;
                }

                bytes[i] = (byte)res;
                var chars = encoding.GetChars(bytes, 0, i + 1)[0];
                if (!((CustomDecoderFallback)encoding.DecoderFallback).IsError) 
                {
                    return chars;
                }
            }

            return null;
        }

        private readonly List<byte> localBuffer;
        private readonly System.Text.Encoding encoding;

        private readonly object locker = new object();
        private ITerminalIOSource source;
    }
}
