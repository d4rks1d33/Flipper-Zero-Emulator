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
using AntShell.Helpers;

namespace AntShell.Terminal
{
    public class BasicTerminalEmulator
    {
        public IOProvider InputOutput { get; protected set; }

        public bool PlainMode { get; set; }

        public BasicTerminalEmulator(IOProvider io)
        {
            InputOutput = io;
        }

        protected void SendControlSequence(params string[] seq)
        {
            foreach(var s in seq)
            {
                foreach(var c in s)
                {
                    InputOutput.Write(c);
                }
            }
        }

        protected void SendControlSequence(params byte[] seq)
        {
            foreach(var b in seq)
            {
                InputOutput.Write(b);
            }
        }

        protected void SendCSI(params byte[] seq)
        {
            SendControlSequence((byte)SequenceElement.ESC, (byte)SequenceElement.CSI);
            SendControlSequence(seq);
        }

        public void ClearScreen()
        {
            if(!PlainMode)
            {
                SendCSI((byte)'2', (byte)'J');
            }
        }

        public void ResetCursor()
        {
            if(!PlainMode)
            {
                SendCSI();
                SendControlSequence("1;1");
                SendControlSequence((byte)'f');
            }
        }

        protected enum SequenceElement : byte
        {
            ESC = 0x1B,
            // <Esc>
            CSI = 0x5B,
            // '['
            SEM = 0x3B,
            // ';'
            INTEGER = 0xFF
        }
    }
}

