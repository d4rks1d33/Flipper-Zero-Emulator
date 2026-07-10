using System;
using System.IO;

namespace FdtSharpWithTests
{
	internal static class Utilities
	{
		public static string GetBinaryLocation(string name)
		{
			return Path.Combine(BinariesDirectory, name);
		}

		private static readonly string BinariesDirectory = "../../Trees";
	}
}

