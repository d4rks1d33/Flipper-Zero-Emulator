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
using System.Text;

namespace AntShell
{
    public class CommandEditor
    {
        #region Constructors

        public CommandEditor()
        {
            bldr = new StringBuilder();
        }

        #endregion

        #region Insert

        public bool InsertCharacter(char c)
        {
            bldr.Insert(Position, c);
            Position++;

            return Position == Length;
        }

        public void SetValue(string v)
        {
            var previousValue = bldr.ToString();
            if(v != previousValue)
            {
                Value = v;
            }
        }

        #endregion

        #region Remove

        public bool RemovePreviousCharacter()
        {
            if(Position > 0)
            {
                bldr.Remove(Position - 1, 1);
                Position--;

                return true;
            }

            return false;
        }

        public bool RemoveNextCharacter()
        {
            if(Position < Length)
            {
                bldr.Remove(Position, 1);

                return true;
            }

            return false;
        }

        public void Clear()
        {
            if(Length > 0)
            {
                bldr.Clear();
                Position = 0;
            }
        }

        public void RemoveToTheEnd()
        {
            if(Position < Length)
            {
                bldr.Remove(Position, Length - Position);
            }
        }

        public int RemoveWord()
        {
            var diff = DiffPrevWord();
            var previousPosition = Position;
            Position -= diff;
		
            if(diff > 0)
            {
                bldr.Remove(Position, previousPosition - Position);
            }

            return diff;
        }

        public int RemoveWordForward()
        {
            var diff = DiffNextWord();
            var previousPosition = Position;

            if(diff > 0)
            {
                bldr.Remove(previousPosition, diff);
            }

            return diff;
        }

        #endregion

        #region Move Cursor

        public bool MoveCharacterForward()
        {
            if(Position < Length)
            {
                Position++;
                return true;
            }

            return false;
        }

        public bool MoveCharacterBackward()
        {
            if(Position > 0)
            {
                Position--;

                return true;
            }

            return false;
        }

        public int MoveHome()
        {
            var previousPosition = Position;
            Position = 0;

            return previousPosition;
        }

        public int MoveEnd()
        {
            var previousPosition = Position;
            Position = Length;

            return Position - previousPosition;
        }

        public int MoveWordForward()
        {
            var diff = DiffNextWord();
            Position += diff;

            return diff;
        }

        public int MoveWordBackward()
        {
            var diff = DiffPrevWord();
            Position -= diff;

            return diff;
        }

        #endregion

        #region ToString

        public string ToString(int startIndex = 0, int? stopIndex = null)
        {
            return bldr.ToString(startIndex, (stopIndex ?? Length) - startIndex);
        }

        public string ToCurrentPositionString()
        {
            return ToString(0, Position);
        }

        #endregion

        #region Properties

        public int Position { get; private set; }

        public int Length { get { return bldr.Length; } }

        public string Value
        { 
            get
            { 
                return ToString(); 
            } 

            private set
            {
                bldr.Clear();
                bldr.Append(value);
                Position = bldr.Length;
            }
        }

        #endregion

        #region Private fields

        private StringBuilder bldr;

        #endregion

        #region Helper methods

        private int DiffNextWord()
        {
            var pos = Position;
			
            // jump to the beginning of next word
            while(pos < Length && char.IsWhiteSpace(bldr[pos]))
            {
                pos++;
            }
			
            // jump to the end of current word
            while(pos < Length && !char.IsWhiteSpace(bldr[pos]))
            {
                pos++;
            }
						
            return pos - Position;
        }

        private int DiffPrevWord()
        {
            var pos = Position;
			
            if(pos > 0)
            {
                pos--;
            }
			
            // jump to the end of previous word
            while(pos > 0 && char.IsWhiteSpace(bldr[pos]))
            {
                pos--;
            }
			
            // jump to the beginning of current word
            while(pos > 0 && !char.IsWhiteSpace(bldr[pos]))
            {
                pos--;
            }
			
            if(pos > 0 && Length > 0)
            {
                pos++;
            }

            return Position - pos;
        }

        #endregion
    }
}

