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
using AntShell.Helpers;

namespace AntShell.Terminal
{
    internal class VirtualCursor
    {
        public VirtualCursor()
        {
            RealPosition = new Position(1, 1);
            MaxPosition = new Position(1, 1);
            MaxReachedPosition = new Position(1, 1);
        }

        public Position RealPosition { get; set; }

        public Position BackPosition
        {
            get
            {
                return RealPosition.X == 1 ? new Position(MaxPosition.X + 1, RealPosition.Y - 1) : RealPosition;
            }
        }

        public Position MaxReachedPosition { get ; private set; }

        public Position MaxPosition { get; private set; }

        public bool IsCursorOutOfLine { get; private set; }

        public bool IsCursorOutOfScreen { get; private set; }

        public void Calibrate(Position pos, Position maxPos)
        {
            RealPosition = pos.Clone();
            MaxReachedPosition = pos.Clone();
            MaxPosition = maxPos.Clone();
        }

        public void MoveUp(int n = 1)
        {
            RealPosition.Y = Math.Max(1, RealPosition.Y - n);
        }

        public bool MoveDown(int n = 1)
        {
            if(RealPosition.Y + n <= MaxPosition.Y)
            {
                RealPosition.Y += n;
                return true;
            }

            RealPosition.Y = MaxPosition.Y;
            return false;
        }

        public VirtualCursorMoveResult MoveForward(int n = 1, bool enableOutOfScreen = true, bool enableWrap = true)
        {
            int result = (int)VirtualCursorMoveResult.NotMoved;
            for(int i = 0; i < n; i++)
            {
                var tmp = MoveForward(enableOutOfScreen, enableWrap);
                if((int)tmp > result)
                {
                    result = (int)tmp;
                }
            }

            return (VirtualCursorMoveResult)result;
        }

        private VirtualCursorMoveResult MoveForward(bool enableOutOfScreen, bool enableWrap)
        {
            var result = VirtualCursorMoveResult.NotMoved;

            if(RealPosition.X < MaxPosition.X)
            {
                RealPosition.X++;
                IsCursorOutOfLine = false;
                IsCursorOutOfScreen = false;
                result = VirtualCursorMoveResult.Moved;
            }
            else if(RealPosition.X == MaxPosition.X)
            {
                if(enableOutOfScreen)
                {
                    RealPosition.X++;
                    IsCursorOutOfLine = true;
                    IsCursorOutOfScreen = (RealPosition.Y == MaxPosition.Y);
                    result = VirtualCursorMoveResult.Moved;
                }
                else
                {
                    RealPosition.X = 1;
                    IsCursorOutOfLine = false;
                    IsCursorOutOfScreen = false;
                    if(RealPosition.Y < MaxPosition.Y)
                    {
                        RealPosition.Y++;
                        result = VirtualCursorMoveResult.LineWrapped;
                    }
                    else
                    {
                        result = VirtualCursorMoveResult.ScreenScrolled;
                    }
                }
            }
            else
            {
                if(enableWrap)
                {
                    RealPosition.X = 2;
                    IsCursorOutOfLine = false;

                    if(RealPosition.Y < MaxPosition.Y)
                    {
                        RealPosition.Y++;
                        result = VirtualCursorMoveResult.LineWrapped;
                    }
                    else
                    {
                        result = VirtualCursorMoveResult.ScreenScrolled;
                    }
                }
                else
                {
                    result = VirtualCursorMoveResult.NotMoved;
                }
            }

            if((RealPosition.Y > MaxReachedPosition.Y) || ((RealPosition.Y == MaxReachedPosition.Y) && (RealPosition.X > MaxReachedPosition.X)))
            {
                MaxReachedPosition.X = RealPosition.X;
                MaxReachedPosition.Y = RealPosition.Y;
            }

            return result;
        }

        public void MoveBackward(int n = 1)
        {
            if(RealPosition.X == 1)
            {
                RealPosition.Y--;
                RealPosition.X = Math.Max(1, BackPosition.X - n);
            }
            else
            {
                RealPosition.X = Math.Max(1, RealPosition.X - n);
            }
        }

        public void SetX(int n)
        {
            RealPosition.X = Math.Max(1, Math.Min(MaxPosition.X, n));
        }

        public void SetY(int n)
        {
            RealPosition.Y = Math.Max(1, Math.Min(MaxPosition.Y, n));
        }

        public Position CalculateMoveForward(int n)
        {
            var linesDown = 0;
            var countLeft = n;
            var currentX = RealPosition.X;

            while(currentX + countLeft >= MaxPosition.X + 1)
            {
                countLeft -= MaxPosition.X + 1 - currentX;
                currentX = 1;
                linesDown++;
            }

            return new Position(currentX - RealPosition.X + countLeft, linesDown);
        }

        public Position CalculateMoveBackward(int n)
        {
            var linesUp = 0;
            var countLeft = n;
            var currentX = RealPosition.X - 1;

            while(countLeft > currentX)
            {
                countLeft -= currentX;
                linesUp++;
                currentX = MaxPosition.X;
            }

            return new Position((currentX - countLeft + 1) - RealPosition.X, -linesUp);
        }
    }

    internal enum VirtualCursorMoveResult
    {
        NotMoved,
        Moved,
        LineWrapped,
        ScreenScrolled
    }
}

