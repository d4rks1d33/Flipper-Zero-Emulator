/*
Copyright (c) 2013-2015 Antmicro <www.antmicro.com>

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
using System.Text;

namespace AntShell.Encoding
{
    public class CustomDecoderFallbackBuffer : DecoderFallbackBuffer
    {
        public CustomDecoderFallbackBuffer()
        {
        }

        private bool _IsError;

        public bool IsError
        { 
            get
            {
                var result = _IsError;
                _IsError = false;
                return result;
            }

            private set
            {
                _IsError = value;
            }
        }

        private char? Replacement
        {
            get;
            set;
        }

        #region implemented abstract members of DecoderFallbackBuffer

        public override bool Fallback(byte[] bytesUnknown, int index)
        {
            IsError = true;
            Replacement = '?';
            return true;
        }

        public override char GetNextChar()
        {
            var result = Replacement;
            Replacement = null;
            return result ?? (char)0;
        }

        public override bool MovePrevious()
        {
            return false;
        }

        public override int Remaining { get { return Replacement.HasValue ? 1 : 0; } }

        #endregion
    }
}

