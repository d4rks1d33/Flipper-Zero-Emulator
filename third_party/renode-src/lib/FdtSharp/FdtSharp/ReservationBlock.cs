using System;

namespace FdtSharp
{
	public struct ReservationBlock
	{
		public ReservationBlock(ulong address, ulong size) : this()
		{
			this.Address = address;
			this.Size = size;
		}		

		public ulong Address { get; private set; }
		public ulong Size { get; private set; }

		public static bool operator==(ReservationBlock one, ReservationBlock another)
		{
			return one.Address == another.Address && one.Size == another.Size;
		}

		public static bool operator!=(ReservationBlock one, ReservationBlock another)
		{
			return !(one == another);
		}

		public override bool Equals(object obj)
		{
			if(obj == null)
				return false;
			if(obj.GetType() != typeof(ReservationBlock))
				return false;
			var other = (ReservationBlock)obj;
			return this == other;
		}
		

		public override int GetHashCode()
		{
			unchecked
			{
				return unchecked((int)((17 * Address) ^ (13 * Size)));
			}
		}

		public override string ToString()
		{
			return string.Format("[ReservationBlock: Address={0}, Size={1}]", Address, Size);
		}		
	}
}

