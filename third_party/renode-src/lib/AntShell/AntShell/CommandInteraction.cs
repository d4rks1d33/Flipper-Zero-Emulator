/*
Copyright (c) 2013 Ant Micro <www.antmicro.com>

Authors:
* Mateusz Holenko (mholenko@antmicro.com)

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using System;
using System.IO;
using AntShell.Commands;
using AntShell.Terminal;

namespace AntShell
{
    public class CommandInteraction : ICommandInteraction
    {
        private NavigableTerminalEmulator terminal;
        private CommandLine cmdLine;

        public CommandInteraction(NavigableTerminalEmulator term)
        {
            terminal = term;
        }

        public CommandInteraction(NavigableTerminalEmulator term, CommandLine cmd) : this(term)
        {
            cmdLine = cmd;
        }

        public void Clear()
        {
            QuitEnvironment = false;
            CommandToExecute = null;
        }

        public bool HasNewInput
        {
            get
            {
                return terminal.InputOutput.HasNewInput;
            }
        }

        #region ICommandInteraction implementation

        public bool QuitEnvironment { get; set; }

        public string CommandToExecute { get; set; }

        public string ReadLine()
        {
            return cmdLine.ReadLine();
        }

        public Stream GetRawInputStream()
        {
            return new IOProviderStreamWrapper(terminal.InputOutput);
        }

        public void Write(char c, ConsoleColor? color = null)
        {
            terminal.WriteRaw(c, color);
        }

        public void WriteError(string error)
        {
            if(!error.EndsWith("\r\n"))
            {
                terminal.WriteRaw(error + "\r\n", ConsoleColor.Red);
            }
            else
            {
                terminal.WriteRaw(error, ConsoleColor.Red);
            }
        }

        public void SaveCursor()
        {
            terminal.SaveCursor();
        }

        public void RestoreCursor()
        {
            terminal.RestoreCursor();
        }

        public void ClearToEnd()
        {
            terminal.ClearDown();
        }

        #endregion

        private class IOProviderStreamWrapper : Stream
        {
            public IOProviderStreamWrapper(IOProvider io)
            {
                this.io = io;
            }

            private readonly IOProvider io;

            public override bool CanRead { get { return true; } }

            public override bool CanSeek { get { return false; } }

            public override bool CanWrite { get { return false; } }

            public override long Length { get { return 1; } }

            public override long Position
            {
                get
                {
                    return 0;
                }

                set
                {
                    throw new NotImplementedException();
                }
            }

            public override void Flush()
            {
                // do nothing
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var b = io.GetNextByte();
                if(b == -1)
                {
                    return 0;
                }
                buffer[offset] = (byte)b;
                return 1;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }
    }
}

