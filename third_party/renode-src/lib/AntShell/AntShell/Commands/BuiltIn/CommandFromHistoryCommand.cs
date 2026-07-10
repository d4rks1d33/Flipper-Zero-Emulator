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
using System.Linq;

namespace AntShell.Commands.BuiltIn
{
    public class CommandFromHistoryCommand : CommandBase
    {
        private readonly CommandHistory history;

        public CommandFromHistoryCommand(CommandHistory h) : base("commandFromHistory", "executes command from history.", "!")
        {
            history = h;
        }

        #region ICommand implementation

        public override int Execute(string[] args, ICommandInteraction writer)
        {
            if(args.Length != 2 || args[1] == string.Empty)
            {
                writer.WriteError(string.Format("Usage: {0} <number>", args[0]));
                return 1;
            }

            int num = 0;
            if(!int.TryParse(args[1], out num))
            {
                if(args[1] == "!")
                {
                    num = -1;
                }
                else
                {
                    writer.WriteError(string.Format("{0} is not a proper index", args[1]));
                    return 2;
                }
            }

            history.RemoveLast();
            var count = history.Items.Count();
            if((num > 0 && num > count) || (num < 0 && -num > count))
            {
                writer.WriteError(string.Format("Command #{0} not found in history", num));
                return 3;
            }

            if(num != 0)
            {
                writer.CommandToExecute = history.Items.ElementAt(num + (num < 0 ? count : -1));
            }
            else
            {
                writer.WriteError(string.Format("Positive or negative command index must be provided"));
                return 4;
            }

            return 0;
        }

		

        #endregion

        #region IOperator implementation

        public char Operator { get { return '!'; } }

        #endregion
    }
}

