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
using AntShell.Terminal;

namespace AntShell
{
    public class Prompt
    {
        public string Text { get; private set; }

        public ConsoleColor Color { get; private set; }

        public Prompt(string text, ConsoleColor color)
        {
            Text = text;
            Color = color;
        }

        public virtual void Write(NavigableTerminalEmulator tem)
        {
            tem.Calibrate();
            tem.Write(Text, Color);
        }
    }

    public class SearchPrompt : Prompt
    {
        public CommandEditor Search { get; private set; }

        public ConsoleColor? SearchColor { get; private set; }

        public int Skip
        {
            get
            {
                return Search.Length - Search.Position + Tail.Length + 1;
            }
        }

        public void SetCommandEditor(CommandEditor searchPattern)
        {
            Search = searchPattern;
        }

        public SearchPrompt(string text, ConsoleColor promptColor, ConsoleColor? searchColor = null) : base(text, promptColor)
        {
            SearchColor = searchColor;

            ArgumentStartIndex = Text.IndexOf("{0}", 0);
            Tail = Text.Substring(ArgumentStartIndex + 3, Text.Length - ArgumentStartIndex - 4);
            Head = Text.Substring(0, ArgumentStartIndex);
        }

        public override void Write(NavigableTerminalEmulator tem)
        {
            tem.Calibrate();
            tem.Write(Head, Color);
            tem.Write(Search.Value, SearchColor);
            tem.Write(Tail, Color);
            tem.CursorBackward(Tail.Length);
        }

        public void Recreate(NavigableTerminalEmulator tem, int shift = 0)
        {
            var st = Search.ToString(Search.Position + shift);

            tem.HideCursor();
            tem.ClearToTheEndOfLine();
            tem.Write(st, SearchColor);
            tem.Write(Tail, Color);
            tem.CursorBackward(Tail.Length + st.Length + shift);
            tem.ShowCursor();
        }

        private int ArgumentStartIndex { get; set; }

        private string Tail { get; set; }

        private string Head { get; set; }

    }
}

