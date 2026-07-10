using System;
using System.Collections.Generic;
using System.Linq;

namespace FdtSharp
{
	internal sealed class StringWriter
	{
		public StringWriter()
		{
			result = new List<byte>();
			alreadyWritten = new Dictionary<string, int>();
		}

		public List<byte> Result
		{
			get
			{
				return result.ToList();
			}
		}

		public int PutString(string str)
		{
			if(alreadyWritten.ContainsKey(str))
			{
				return alreadyWritten[str];
			}
			// our string can also be a substring of some another string
			foreach(var cached in alreadyWritten)
			{
				var subIndex = cached.Key.IndexOf(str, StringComparison.Ordinal);
				if(subIndex != -1 && subIndex + str.Length == cached.Key.Length)
				{
					var newStringIndex = cached.Value + subIndex;
					alreadyWritten.Add(str, newStringIndex);
					return newStringIndex;
				}
			}
			var index = result.Count;
			alreadyWritten.Add(str, index);
			result.AddRange(str.NullTerminated());
			return index;
		}

		private readonly List<byte> result;
		private readonly Dictionary<string, int> alreadyWritten;
	}
}

