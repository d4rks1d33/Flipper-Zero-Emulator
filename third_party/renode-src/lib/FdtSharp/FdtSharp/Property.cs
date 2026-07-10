using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Linq;

namespace FdtSharp
{
	public sealed class Property
	{
		public Property(string name, byte[] data)
		{
			this.Name = name;
			this.Data = new ReadOnlyCollection<byte>(data);
		}

		public byte[] GetDataAsBytes()
		{
			return Data.ToArray();
		}

		public void PutDataAsBytes(byte[] data)
		{
			Data = new ReadOnlyCollection<byte>(data.ToArray());
		}

		public string GetDataAsString()
		{
			return Encoding.UTF8.GetString(GetDataAsBytes(), 0, Data.Count - 1);
		}

		public void PutDataAsString(string data)
		{
			Data = new ReadOnlyCollection<byte>(data.NullTerminated());
		}

		public string Name { get; private set; }
		public IReadOnlyCollection<byte> Data { get; private set; }
	}
}

