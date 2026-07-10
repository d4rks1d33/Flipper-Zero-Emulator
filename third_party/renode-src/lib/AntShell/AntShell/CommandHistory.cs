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
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AntShell
{
    public class CommandHistory
    {
        #region Add

        public void Add(string command)
        {
            if(items.Count == 0 || items[items.Count - 1] != command)
            {
                items.Add(command);
                position = items.Count;
            }
        }

        #endregion

        #region Remove

        public void RemoveLast()
        {
            items.RemoveAt(items.Count - 1);
            Reset();
        }

        #endregion

        #region Search

        public string Search(string command)
        {
            var current = items.ElementAtOrDefault(position);
            string found;
            do
            {
                found = searchForward ? ForwardSearch(command) : ReverseSearch(command);
            } while(found == current && found != null);
            return found;
        }

        public string ReverseSearch(string command)
        {
            if(command != string.Empty)
            {
                int lPos = position - 1;
                while(lPos >= 0)
                {
                    if(items[lPos].Contains(command))
                    {
                        position = lPos;
                        return items[lPos];
                    }
					
                    lPos--;
                }
            }

            position = 0;
            return null;
        }

        public string ForwardSearch(string command)
        {
            if(command != string.Empty)
            {
                int lPos = position + 1;
                while(lPos < items.Count)
                {
                    if(items[lPos].Contains(command))
                    {
                        position = lPos;
                        return items[lPos];
                    }

                    lPos++;
                }
            }

            position = items.Count;
            return null;
        }

        #endregion

        #region Iterate methods

        public string PreviousCommand()
        {
            if(position > 0)
            {
                position--;
                return items[position];
            }
			
            return null;
        }

        public string CurrentCommand
        {
            get
            {
                return HasMoved ? items[position] : null;
            }
        }

        public string NextCommand()
        {
            if(position != -1 && position < items.Count - 1)
            {
                position++;
                return items[position];
                ;
            }

            position = items.Count;
            return _currentCommand;
        }

        public void Reset()
        {
            position = items.Count;
            _currentCommand = null;
        }

        public void SetCurrentCommand(string value)
        {
            _currentCommand = value;
        }

        #endregion

        public bool searchForward = false;

        #region Private fields

        private List<string> items = new List<string>();
		
        private int position = -1;

        private string _currentCommand;

        #endregion

        #region Properties

        public IEnumerable<string> Items { get { return items; } }

        public bool HasMoved { get { return (position != -1) && (position != items.Count); } }

        #endregion

        #region Save/Load

        public void Save(string path)
        {
            if(items.Count > 0 && (items[items.Count - 1] == "q" || items[items.Count - 1] == "quit"))
            {
                items.RemoveAt(items.Count - 1);
            }

            File.WriteAllLines(path, Items);
        }

        public void Load(string path)
        {
            if(File.Exists(path))
            {
                items.AddRange(File.ReadAllLines(path));
                position = items.Count;
            }
        }

        #endregion
    }
}

