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
using AntShell.Commands;

namespace AntShellDemo
{
    public class AskCommand : CommandBase
	{
		#region ICommand implementation

        public override int Execute(string[] args, ICommandInteraction writer)
		{
			if (args.Length != 1)
			{
				writer.WriteError("Command usage: Ask");
				return 1;
			}

			writer.Write("What should I add? ");
			var v = writer.ReadLine();
			if (v != null)
			{
				var val = int.Parse(v);
				Calculator.Add(val);
			}

			return 0;
		}

		#endregion

        public AskCommand() : base("Ask", "Asks for a value to add") {}
	}
}

