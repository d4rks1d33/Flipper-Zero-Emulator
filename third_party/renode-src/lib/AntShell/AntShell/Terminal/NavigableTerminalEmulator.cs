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
using System.Collections.Generic;
using System.Linq;
using AntShell.Helpers;

namespace AntShell.Terminal
{
    public class NavigableTerminalEmulator : BasicTerminalEmulator
    {
        private const int MAX_HEIGHT = 9999;
        private const int MAX_WIDTH = 9999;

        private SequenceValidator validator;
        private Queue<char> sequenceQueue;

        public ITerminalHandler Handler { get; set; }

        private VirtualCursor vcursor;

        private List<int> WrappedLines;
        private int CurrentLine;
        private int LinesScrolled;

        private bool onceAgain;

        private bool clearScreen = false;
        private bool forceVirtualCursor = false;

        public NavigableTerminalEmulator(IOProvider io, bool forceVCursor = false) : base(io)
        {
            validator = new SequenceValidator();
            sequenceQueue = new Queue<char>();

            forceVirtualCursor = forceVCursor;
            WrappedLines = new List<int>();
            vcursor = new VirtualCursor();

            ControlSequences();
        }

        public void Start(bool clearScreen)
        {
            this.clearScreen = clearScreen;
            if(clearScreen)
            {
                ClearScreen();
                ResetCursor();
            }
            ResetColors();
        }

        private void ControlSequences()
        {
            validator.Add(ControlSequenceType.LeftArrow, (char)SequenceElement.ESC, (char)SequenceElement.CSI, 'D');
            validator.Add(ControlSequenceType.RightArrow, (char)SequenceElement.ESC, (char)SequenceElement.CSI, 'C');
            validator.Add(ControlSequenceType.UpArrow, (char)SequenceElement.ESC, (char)SequenceElement.CSI, 'A');
            validator.Add(ControlSequenceType.DownArrow, (char)SequenceElement.ESC, (char)SequenceElement.CSI, 'B');

            validator.Add(ControlSequenceType.LeftArrow, (char)SequenceElement.ESC, 'O', 'D');
            validator.Add(ControlSequenceType.RightArrow, (char)SequenceElement.ESC, 'O', 'C');
            validator.Add(ControlSequenceType.UpArrow, (char)SequenceElement.ESC, 'O', 'A');
            validator.Add(ControlSequenceType.DownArrow, (char)SequenceElement.ESC, 'O', 'B');

            validator.Add(ControlSequenceType.CtrlLeftArrow, (char)SequenceElement.ESC, (char)SequenceElement.CSI, '1', ';', '5', 'D');
            validator.Add(ControlSequenceType.CtrlRightArrow, (char)SequenceElement.ESC, (char)SequenceElement.CSI, '1', ';', '5', 'C');
            // Alt is sent as an ESC prefix in terminal emulators, and ESC treats lowercase/uppercase differently,
            // which is why we handle both cases separately
            validator.Add(ControlSequenceType.CtrlRightArrow, (char)SequenceElement.ESC, 'f');
            validator.Add(ControlSequenceType.CtrlRightArrow, (char)SequenceElement.ESC, 'F');
            validator.Add(ControlSequenceType.CtrlLeftArrow, (char)SequenceElement.ESC, 'b');
            validator.Add(ControlSequenceType.CtrlLeftArrow, (char)SequenceElement.ESC, 'B');

            validator.Add(ControlSequenceType.AltBackspace, (char)SequenceElement.ESC, (char)0x7f);
            validator.Add(ControlSequenceType.AltD, (char)SequenceElement.ESC, (char)'d');
            validator.Add(ControlSequenceType.AltD, (char)SequenceElement.ESC, (char)'D');

            validator.Add(ControlSequenceType.Delete, (char)SequenceElement.ESC, (char)SequenceElement.CSI, '3', '~');

            validator.Add(ControlSequenceType.Home, (char)SequenceElement.ESC, (char)SequenceElement.CSI, '1', '~');
            validator.Add(ControlSequenceType.Home, (char)SequenceElement.ESC, 'O', 'H');
            validator.Add(ControlSequenceType.Home, (char)SequenceElement.ESC, (char)SequenceElement.CSI, 'H');

            validator.Add(ControlSequenceType.End, (char)SequenceElement.ESC, (char)SequenceElement.CSI, '4', '~');
            validator.Add(ControlSequenceType.End, (char)SequenceElement.ESC, 'O', 'F');
            validator.Add(ControlSequenceType.End, (char)SequenceElement.ESC, (char)SequenceElement.CSI, 'F');

            validator.Add(ControlSequenceType.Esc, (char)SequenceElement.ESC, (char)SequenceElement.ESC);

            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'k'), (char)11);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'r'), (char)18);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 's'), (char)19);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'w'), (char)23);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'c'), (char)3);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'd'), (char)4);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'u'), (char)21);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'a'), (char)1);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'e'), (char)5);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'f'), (char)6);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'b'), (char)2);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'p'), (char)16);
            validator.Add(new ControlSequence(ControlSequenceType.Ctrl, 'n'), (char)14);

            validator.Add(ControlSequenceType.Tab, '\t');
            validator.Add(ControlSequenceType.Backspace, (char)127);
            validator.Add(ControlSequenceType.Backspace, (char)8);
            validator.Add(ControlSequenceType.Enter, '\r');
            validator.Add(ControlSequenceType.Enter, '\n');

            validator.Add(ControlSequenceType.Ignore, (char)12);

            validator.Add((ControlSequenceGenerator)((s, l) =>
            {
                var str = new string(s.ToArray());
                var trimmed = str.Substring(2, str.Length - 3);
                var splitted = trimmed.Split(';');

                return new ControlSequence(ControlSequenceType.CursorPosition, new Position(int.Parse(splitted[1]), int.Parse(splitted[0])));
            }),
                (char)SequenceElement.ESC, (char)SequenceElement.CSI, (char)SequenceElement.INTEGER, ';', (char)SequenceElement.INTEGER, 'R');
        }

        #region Input handling

        public void Stop()
        {
            if(clearScreen)
            {
                ClearScreen();
                CursorToColumn(1);
                CursorUp(MAX_HEIGHT);
            }
            ResetColors();

            onceAgain = false;
            InputOutput.CancelGet();
            InputOutput.Dispose();
        }

        public void Run(bool stopOnError = false)
        {
            onceAgain = true;
            while(onceAgain)
            {
                var input = GetNextInput();

                if(input == null)
                {
                    if(stopOnError)
                    {
                        break;
                    }

                    continue;
                }

                HandleInput(input);	
            }
        }

        public object GetNextInput()
        {
            while(true)
            {
                var b = InputOutput.GetNextChar();
                if(b == null)
                {
                    return null;
                }

                if(sequenceQueue.Count == 0)
                {
                    if(b == (char)SequenceElement.ESC)
                    {
                        sequenceQueue.Enqueue(b.Value);
                        continue;
                    }

                    // try special control sequence
                    ControlSequence cs;
                    var result = validator.Check(new [] { b.Value }, out cs);
                    if(result == SequenceValidationResult.SequenceFound)
                    {
                        return cs;
                    }

                    // so it must be normal character
                    return b.Value;
                }
                else
                {
                    sequenceQueue.Enqueue(b.Value);
                    ControlSequence cs;
                    var validationResult = validator.Check(sequenceQueue.ToArray(), out cs);
                    if(cs == null)
                    {
                        if(validationResult == SequenceValidationResult.SequenceNotFound)
                        {
                            sequenceQueue.Clear();
                        }
                    }
                    else
                    {
                        sequenceQueue.Clear();
                        return cs;
                    }
                }
            }
        }

        private void HandleInput(object input)
        {
            if(input == null)
            {
                return;
            }

            var inputAsControlSequence = input as ControlSequence;
            if(input is char)
            {
                Handler.HandleCharacter((char)input);
            }
            else if(inputAsControlSequence != null)
            {
                Handler.HandleControlSequence((ControlSequence)input);
            }
        }

        #endregion

        private void OnScreenScroll()
        {
            LinesScrolled++;
        }

        private void OnLineWrapped()
        {
            WrappedLines.Add(vcursor.MaxPosition.X + 1);
            CurrentLine++;
        }

        #region Writers

        internal int Write(string text, ConsoleColor? color = null)
        {
            var result = 0;

            if(text != null)
            {
                ColorChangerWrapper(color, () =>
                {
                    foreach(var c in text)
                    {
                        result += WriteChar(c) ? 1 : 0;
                    }
                });
            }

            return result;
        }

        public void Write(char c, ConsoleColor? color = null)
        {
            ColorChangerWrapper(color, () => WriteChar(c));
        }

        public void WriteNoMove(string text, int skip = 0, ConsoleColor? color = null)
        {
            if(text != null)
            {
                var ep = vcursor.RealPosition;
                var currline = CurrentLine;

                HideCursor();

                CursorForward(skip);
                var count = Write(text, color);
                CursorBackward(count + skip);

                ShowCursor();

                vcursor.RealPosition = ep;
                CurrentLine = currline;
            }
        }

        public void WriteRaw(char c, ConsoleColor? color = null)
        {
            ColorChangerWrapper(color, () => InputOutput.Write(c));
        }

        public void WriteRaw(byte[] bs, ConsoleColor? color = null)
        {
            ColorChangerWrapper(color, () =>
            {
                foreach(var b in bs)
                {
                    InputOutput.Write(b);
                }
            }
            );
        }

        public void WriteRaw(string text, ConsoleColor? color = null)
        {
            if(text != null)
            {
                ColorChangerWrapper(color, () =>
                {
                    foreach(var b in text)
                    {
                        InputOutput.Write(b);
                    }
                }
                );
            }
        }

        private void ColorChangerWrapper(ConsoleColor? color, Action action)
        {
            if(color.HasValue)
            {
                SetColor(color.Value);
            }

            action();

            if(color.HasValue)
            {
                ResetColors();
            }
        }

        private bool InEscapeMode = false;

        private bool WriteChar(char c)
        {
            InputOutput.Write(c);

            if(c == (byte)SequenceElement.ESC) // to eliminate control sequences, mostly color change
            {
                InEscapeMode = true;
            }

            if(!InEscapeMode) // if the char changed cursor position by one; check for color steering codes
            {
                if(c == '\r')
                {
                    vcursor.RealPosition.X = 1;
                }
                else if(c == '\n')
                {
                    var scrolled = !vcursor.MoveDown();
                    if(scrolled)
                    {
                        OnScreenScroll();
                    }
                }
                else
                {
                    var result = vcursor.MoveForward();

                    if(vcursor.IsCursorOutOfLine && !vcursor.IsCursorOutOfScreen)
                    {
                        CursorDown();
                        CursorToColumn(1);
                    }

                    if(result == VirtualCursorMoveResult.LineWrapped)
                    {
                        OnLineWrapped();
                    }

                    if(result == VirtualCursorMoveResult.ScreenScrolled)
                    {
                        OnLineWrapped();
                        OnScreenScroll();
                    }
                }

                return true;
            }
            else
            {
                if(c == 'm')
                {
                    InEscapeMode = false;
                }
            }

            return false;
        }

        #endregion

        #region Cursor movement

        public void CursorUp(int n = 1, bool recalculateEstimatedPosition = true)
        {
            if(n > 0)
            {
                SendCSI();
                if(n > 1)
                {
                    SendControlSequence(n.ToString());
                }

                SendControlSequence((byte)'A');

                if(recalculateEstimatedPosition)
                {
                    vcursor.MoveUp(n);
                }
            }
        }

        public void CursorDown(int n = 1, bool recalculateEstimatedPosition = true)
        {
            if(n > 0)
            {
                SendCSI();
                if(n > 1)
                {
                    SendControlSequence(n.ToString());
                }

                SendControlSequence((byte)'B');

                if(recalculateEstimatedPosition)
                {
                    vcursor.MoveDown(n);
                }
            }
        }

        public void CursorForward(int n = 1)
        {
            if(n > 0)
            {
                var move = vcursor.CalculateMoveForward(n);

                CursorDown(move.Y);

                n = Math.Abs(move.X);

                if(n != 0)
                {
                    SendCSI();
                }
                if(n > 1)
                {
                    SendControlSequence(n.ToString());
                }
                if(move.X > 0)
                {
                    SendControlSequence((byte)'C');
                    vcursor.MoveForward(n, true);
                }
                else if(move.X < 0)
                {
                    SendControlSequence((byte)'D');
                    vcursor.MoveBackward(n);
                }

                CurrentLine = vcursor.RealPosition.X == 1 ? CurrentLine - (move.Y - 1) : CurrentLine - move.Y;

                if(vcursor.IsCursorOutOfScreen)
                {
                    ScrollDown();
                }
            }
        }

        public void CursorBackward(int n = 1)
        {
            if(n > 0)
            {
                var vb = (vcursor.RealPosition.X == 1);
                var move = vcursor.CalculateMoveBackward(n);

                CurrentLine = Math.Max(0, CurrentLine + (vb ? move.Y + 1 : move.Y));

                CursorUp(-move.Y);
                n = Math.Abs(move.X);

                if(n != 0)
                {
                    SendCSI();
                }
                if(n > 1)
                {
                    SendControlSequence(n.ToString());
                }
                if(move.X < 0)
                {
                    SendControlSequence((byte)'D');
                    vcursor.MoveBackward(n);
                }
                else if(move.X > 0)
                {
                    SendControlSequence((byte)'C');
                    vcursor.MoveForward(n, false);
                }
            }
        }

        public void CursorToColumn(int n, bool recalculateEstimatedPosition = true)
        {
            SendCSI();
            SendControlSequence(n.ToString());
            SendControlSequence((byte)'G');

            if(recalculateEstimatedPosition)
            {
                vcursor.SetX(n);
            }
        }

        public void NewLine()
        {
            Write("\n\r");
            CurrentLine = 0;
            WrappedLines.Clear();

        }

        #endregion

        #region Erase

        public void ClearLine()
        {
            SendCSI((byte)'2', (byte)'K');
            CursorToColumn(0);

            var count = vcursor.MaxReachedPosition.Y - vcursor.RealPosition.Y;
            CursorDown(count);
            for(int i = count; i > 0; i--)
            {
                SendCSI((byte)'2', (byte)'K'); // clear line
                CursorUp();
                if(WrappedLines.Count - i >= 0)
                {
                    WrappedLines.RemoveAt(WrappedLines.Count - i);
                }
            }

            CurrentLine = 0;

            vcursor.MaxReachedPosition.X = vcursor.RealPosition.X;
            vcursor.MaxReachedPosition.Y = vcursor.RealPosition.Y;
        }

        public void ClearToTheEndOfLine()
        {
            var count = vcursor.MaxReachedPosition.Y - vcursor.RealPosition.Y;
            CursorDown(count);
            for(int i = count; i > 0; i--)
            {
                SendCSI((byte)'2', (byte)'K'); // clear line
                CursorUp();		

                if(WrappedLines.Count > 0 && WrappedLines.Count - i > 0)
                {
                    WrappedLines.RemoveAt(WrappedLines.Count - i);
                }
            }

            SendCSI((byte)'K');

            vcursor.MaxReachedPosition.X = vcursor.RealPosition.X;
            vcursor.MaxReachedPosition.Y = vcursor.RealPosition.Y;
        }

        public void ClearDown()
        {
            SendCSI((byte)'J');
        }

        #endregion

        #region Display

        public void ResetColors()
        {
            if(PlainMode)
            {
                return;
            }
            SendCSI((byte)'0', (byte)'m'); // reset colors
        }

        public void Calibrate()
        {
            if(forceVirtualCursor)
            {
                vcursor.Calibrate(new Position(0, 0), new Position(MAX_WIDTH, MAX_HEIGHT));
            }
            else
            {
                vcursor.Calibrate(GetCursorPosition(), GetSize());
            }
        }

        public void ScrollDown()
        {
            SendControlSequence((byte)SequenceElement.ESC, (byte)'D');
        }

        private Position savedPosition;
        private int scrollCount;

        public void SaveCursor()
        {
            savedPosition = vcursor.RealPosition.Clone();
            scrollCount = LinesScrolled;
            SendCSI((byte)'s');
        }

        public void RestoreCursor()
        {
            SendCSI((byte)'u');
            vcursor.RealPosition = savedPosition.Clone();

            var scrolled = LinesScrolled - scrollCount;
            CursorUp(scrolled);
        }

        public void HideCursor()
        {
            SendCSI();
            SendControlSequence("?25l");
        }

        public void ShowCursor()
        {
            SendCSI();
            SendControlSequence("?25h");
        }

        public void SetColor(ConsoleColor color)
        {
            if(PlainMode)
            {
                return;
            }
            switch(color)
            {
            case ConsoleColor.Black:
                SendCSI((byte)SequenceElement.SEM, (byte)'0', (byte)'3', (byte)'0', (byte)'m');
                break;
            case ConsoleColor.Red:
                SendCSI((byte)SequenceElement.SEM, (byte)'0', (byte)'3', (byte)'1', (byte)'m');
                break;
            case ConsoleColor.Green:
                SendCSI((byte)SequenceElement.SEM, (byte)'0', (byte)'3', (byte)'2', (byte)'m');
                break;
            case ConsoleColor.Yellow:
                SendCSI((byte)SequenceElement.SEM, (byte)'0', (byte)'3', (byte)'3', (byte)'m');
                break;
            case ConsoleColor.Blue:
                SendCSI((byte)SequenceElement.SEM, (byte)'0', (byte)'3', (byte)'4', (byte)'m');
                break;
            case ConsoleColor.Magenta:
                SendCSI((byte)SequenceElement.SEM, (byte)'0', (byte)'3', (byte)'5', (byte)'m');
                break;
            case ConsoleColor.Cyan:
                SendCSI((byte)SequenceElement.SEM, (byte)'0', (byte)'3', (byte)'6', (byte)'m');
                break;
            case ConsoleColor.Gray:
                SendCSI((byte)SequenceElement.SEM, (byte)'0', (byte)'3', (byte)'7', (byte)'m');
                break;

            case ConsoleColor.DarkGray:
                SendCSI((byte)'3', (byte)'0', (byte)SequenceElement.SEM, (byte)'1', (byte)'m');
                break;
            case ConsoleColor.DarkRed:
                SendCSI((byte)'3', (byte)'1', (byte)SequenceElement.SEM, (byte)'1', (byte)'m');
                break;
            case ConsoleColor.DarkGreen:
                SendCSI((byte)'3', (byte)'2', (byte)SequenceElement.SEM, (byte)'1', (byte)'m');
                break;
            case ConsoleColor.DarkYellow:
                SendCSI((byte)'3', (byte)'3', (byte)SequenceElement.SEM, (byte)'1', (byte)'m');
                break;
            case ConsoleColor.DarkBlue:
                SendCSI((byte)'3', (byte)'4', (byte)SequenceElement.SEM, (byte)'1', (byte)'m');
                break;
            case ConsoleColor.DarkMagenta:
                SendCSI((byte)'3', (byte)'5', (byte)SequenceElement.SEM, (byte)'1', (byte)'m');
                break;
            case ConsoleColor.DarkCyan:
                SendCSI((byte)'3', (byte)'6', (byte)SequenceElement.SEM, (byte)'1', (byte)'m');
                break;
            case ConsoleColor.White:
                SendCSI((byte)'3', (byte)'7', (byte)SequenceElement.SEM, (byte)'1', (byte)'m');
                break;
            }
        }

        public Position GetSize()
        {
            HideCursor();
            SaveCursor();
            CursorToColumn(MAX_WIDTH, false);
            CursorDown(MAX_HEIGHT, false);

            var result = GetCursorPosition();

            RestoreCursor();
            ShowCursor();
            return result;
        }

        public Position GetCursorPosition()
        {
            if(forceVirtualCursor)
            {
                return vcursor.RealPosition.Clone();
            }

            var unusedCharacters = new List<char>();
            ControlSequence cs;
            while(true)
            {
                var localBuffer = new List<char>();

                SendCSI();
                SendControlSequence("6n");

                while(true)
                {
                    var currentChar = InputOutput.GetNextChar();
                    if(currentChar == null)
                    {
                        foreach(var uc in unusedCharacters)
                        {
                            InputOutput.Inject(uc);
                        }
                        // the output is closed - there will be no more data
                        return new Position(0, 0);
                    }

                    localBuffer.Add(currentChar.Value);

                    var validationResult = validator.Check(localBuffer, out cs);
                    switch(validationResult)
                    {
                    case SequenceValidationResult.PrefixFound:
                        continue;

                    case SequenceValidationResult.SequenceFound:
                        if(cs.Type == ControlSequenceType.CursorPosition)
                        {
                            foreach(var uc in unusedCharacters)
                            {
                                InputOutput.Inject(uc);
                            }
                            return cs.Argument as Position;
                        }
                        break;
                    }
                    unusedCharacters.AddRange(localBuffer);
                    localBuffer.Clear();
                }
            }
        }

        #endregion
    }
}

