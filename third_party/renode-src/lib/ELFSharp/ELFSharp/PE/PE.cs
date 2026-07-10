using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;

namespace ELFSharp.PE
{
    public class PE
    {
        public PE(BinaryReader reader)
        {
            Init(reader);
        }

        public PE(string path)
        {
            using(var reader = new BinaryReader(File.OpenRead(path)))
            {
                Init(reader);
            }
        }

        public string[] GetExportedSymbols()
        {
            return namesTable.GetEntriesNames();
        }

        private void Init(BinaryReader reader)
        {
            reader.BaseStream.Seek(0x40, SeekOrigin.Current); // skip image dos header
            reader.BaseStream.Seek(0x40, SeekOrigin.Current); // skip "This program cannot be run in DOS mode" part
            reader.BaseStream.Seek(0x4, SeekOrigin.Current); // skip PE signature

            fileHeader = StreamToStructure<ImageFileHeader>(reader);
            reader.BaseStream.Seek(fileHeader.Is32BitHeader ? 0xE0 : 0xF0, SeekOrigin.Current); // skip optional header

            sectionHeaders = new ImageSectionHeader[fileHeader.NumberOfSections];
            for(var i = 0; i < sectionHeaders.Length; i++)
            {
                sectionHeaders[i] = StreamToStructure<ImageSectionHeader>(reader);
            }

            var exportDataSection = sectionHeaders.Single(x => x.Section == ExportDataSectionName);
            reader.BaseStream.Seek(exportDataSection.PointerToRawData, SeekOrigin.Begin);
            var exportDirectory = StreamToStructure<ImageExportDirectory>(reader);
            var namesTableFileOffset = exportDirectory.AddressOfNames - exportDataSection.VirtualAddress + exportDataSection.PointerToRawData;
            reader.BaseStream.Seek(namesTableFileOffset, SeekOrigin.Begin);
            namesTable = new NamesTable(reader, exportDirectory.NumberOfNames, exportDataSection.PointerToRawData - exportDataSection.VirtualAddress);
        }

        private static T StreamToStructure<T>(BinaryReader reader) where T : struct
        {
            var bytes = reader.ReadBytes(Marshal.SizeOf(typeof(T)));
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var result = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();
            return result;
        }

        private ImageFileHeader fileHeader;
        private ImageSectionHeader[] sectionHeaders;
        private NamesTable namesTable;

        private static string ExportDataSectionName = ".edata";
    }
}

