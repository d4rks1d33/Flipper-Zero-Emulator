using System;
using System.IO;

namespace ELFSharp.PE
{
    public static class PEReader
    {
        public static bool TryLoad(string fileName, out PE pe)
        {
            pe = null;
            using(var reader = new BinaryReader(File.OpenRead(fileName)))
            {
                var magic = reader.ReadUInt16();
                if(magic != 0x5a4d)
                {
                    return false;
                }

                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                pe = new PE(reader);
            }
            return true;
        }
    }
}

