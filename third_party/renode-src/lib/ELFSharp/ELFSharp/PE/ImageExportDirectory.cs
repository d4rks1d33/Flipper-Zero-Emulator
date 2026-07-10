using System;
using System.Runtime.InteropServices;

namespace ELFSharp.PE
{
    // Struct layout based on: http://www.pinvoke.net/default.aspx/Structures/IMAGE_EXPORT_DIRECTORY.html
    [StructLayout(LayoutKind.Sequential)]
    public struct ImageExportDirectory
    {
        public UInt32 Characteristics;
        public UInt32 TimeDateStamp;
        public UInt16 MajorVersion;
        public UInt16 MinorVersion;
        public UInt32 Name;
        public UInt32 Base;
        public UInt32 NumberOfFunctions;
        public UInt32 NumberOfNames;
        public UInt32 AddressOfFunctions;     // RVA (Relative Virtual Address) from base of image
        public UInt32 AddressOfNames;         // RVA (Relative Virtual Address) from base of image
        public UInt32 AddressOfNameOrdinals;  // RVA (Relative Virtual Address) from base of image
    }

}

