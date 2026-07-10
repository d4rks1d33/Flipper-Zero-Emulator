using System;

namespace ELFSharp.ELF.Sections
{
	public class SymbolEntry<T> : ISymbolEntry where T : struct
	{
		public SymbolEntry<T> OffsetBy(T offset)
		{
			T offsetValue;
			if(Value is uint)
			{
				// We know T is uint from above. We can't use `if(Value is ulong valueUlong)` because that
				// results in invalid code generation on Mono 6.8.0.105+dfsg-3.3. The generated IL unconditionally
				// attempts to store `Value` directly into a local of type int32 (copying it to a local first doesn't help).
				var valueUint = (uint)(object)Value;
				var offsetUint = (uint)(object)offset;
				if(offsetUint == 0)
				{
					return this;
				}
				offsetValue = (T)(object)(valueUint + offsetUint);
			}
			else if(Value is ulong)
			{
				// See the comment above
				var valueUlong = (ulong)(object)Value;
				var offsetUlong = (ulong)(object)offset;
				if(offsetUlong == 0)
				{
					return this;
				}
				offsetValue = (T)(object)(valueUlong + offsetUlong);
			}
			else
			{
				throw new ArgumentOutOfRangeException(nameof(offset), "Unknown ELF width");
			}
			return new SymbolEntry<T>(Name, offsetValue, Size, Binding, Type, elf, sectionIdx);
		}

		public string Name { get; private set; }

		public SymbolBinding Binding { get; private set; }

		public SymbolType Type { get; private set; }

		public T Value { get; private set; }

		public T Size { get; private set; }

		public bool IsPointedIndexSpecial
		{
			get { return Enum.IsDefined(typeof(SpecialSectionIndex), sectionIdx); }
		}

		public Section<T> PointedSection
		{
			get { return IsPointedIndexSpecial ? null : elf.GetSection(sectionIdx); }
		}

		ISection ISymbolEntry.PointedSection
		{
			get { return PointedSection; }
		}

		public ushort PointedSectionIndex
		{ 
			get { return sectionIdx; }
		}

		public SpecialSectionIndex SpecialPointedSectionIndex
		{
			get
			{
				if(IsPointedIndexSpecial)
				{
					return (SpecialSectionIndex)sectionIdx;
				}
				throw new InvalidOperationException("Given pointed section index does not have special meaning.");
			}
		}

		public override string ToString()
		{
			return string.Format("[{3} {4} {0}: 0x{1:X}, size: {2}, section idx: {5}]",
                                 Name, Value, Size, Binding, Type, (SpecialSectionIndex)sectionIdx);
		}

		public SymbolEntry(string name, T value, T size, SymbolBinding binding, SymbolType type, ELF<T> elf, ushort sectionIdx)
		{
			Name = name;
			Value = value;
			Size = size;
			Binding = binding;
			Type = type;
			this.elf = elf;
			this.sectionIdx = sectionIdx;
		}

		private readonly ELF<T> elf;
		private readonly ushort sectionIdx;
	}
}