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
using System.Runtime.InteropServices;
using Mono.Unix;
using Mono.Unix.Native;
using System.IO;

namespace AntShellDemo
{
	public class Pty
	{
		private IntPtr original;
		private int master;

		public Pty(string name)
		{
			master = Syscall.open(name, OpenFlags.O_RDWR);
			stream = new UnixStream(master, true);
			IntPtr termios = Marshal.AllocHGlobal(128); // termios struct is 60-bytes, but we allocate more just to make sure
			original = Marshal.AllocHGlobal(128);
			Tcgetattr(0, termios);
			Tcgetattr(0, original);

			Cfmakeraw(termios);
			Tcsetattr(master, 1, termios);  // 1 == TCSAFLUSH
			Marshal.FreeHGlobal(termios);
		}

		public void ResetTerminal()
		{
			Tcsetattr(master, 1, original);  // 1 == TCSAFLUSH
			Marshal.FreeHGlobal(original);
		}

		private UnixStream stream;

		[DllImport("libc", EntryPoint = "cfmakeraw")]
		private extern static void Cfmakeraw(IntPtr termios); // TODO: this is non-posix, but should work on BSD

		[DllImport("libc", EntryPoint = "tcgetattr")]
		private extern static void Tcgetattr(int fd, IntPtr termios);

		[DllImport("libc", EntryPoint = "tcsetattr")]
		private extern static void Tcsetattr(int fd, int attr, IntPtr termios);

		#region Implementation of IStreamProvider

		public Stream Stream { get {return stream;} }

		#endregion
	}
}

