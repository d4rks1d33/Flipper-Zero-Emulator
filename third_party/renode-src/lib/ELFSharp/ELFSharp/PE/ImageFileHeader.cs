using System;
using System.Runtime.InteropServices;

namespace ELFSharp.PE
{
    // Struct layout based on: http://www.pinvoke.net/default.aspx/Structures/IMAGE_FILE_HEADER.html
    [StructLayout(LayoutKind.Sequential)]
    public struct ImageFileHeader
    {
        public UInt16 Machine;
        public UInt16 NumberOfSections;
        public UInt32 TimeDateStamp;
        public UInt32 PointerToSymbolTable;
        public UInt32 NumberOfSymbols;
        public UInt16 SizeOfOptionalHeader;
        public UInt16 Characteristics;

        public bool Is32BitHeader 
        {
            get 
            {
                UInt16 IMAGE_FILE_32BIT_MACHINE = 0x0100;
                return (IMAGE_FILE_32BIT_MACHINE & Characteristics) == IMAGE_FILE_32BIT_MACHINE;
            }
        }
    }
}

