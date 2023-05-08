using PBXMLDecoder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WiiUStreamTool
{
    public class WiiUStream
    {
        public enum SkinFlag : short
        {
            Knuckles_Alt = -4,
            Amy_Alt = -3,
            Tails_Alt,
            Sonic_Alt,
            Default,
            Sonic_Default,
            Tails_Default,
            Amy_Default,
            Knuckles_Default
        }
        public class FileEntry
        {
            public uint CompressedSize;
            public uint DecompressedSize;
            public uint Hash;
            public ushort Unknown;
            public SkinFlag Flag;
            public string Name;
            public byte[] Data;
        }

        public const int Signature = 0x7374726d;
        public const string Extension = ".wiiu.stream";
        public List<FileEntry> Entries = new List<FileEntry>();

        public void Read(string inFile, bool decompressXML = false)
        {
            using var reader = new NativeReader(File.OpenRead(inFile));

            if (reader.ReadUInt(Endian.Big) != Signature) // 'strm'
                throw new InvalidDataException();

            while (reader.BaseStream.Position < reader.Length)
            {
                var entry = new FileEntry();

                entry.CompressedSize = reader.ReadUInt(Endian.Big);
                entry.DecompressedSize = reader.ReadUInt(Endian.Big);
                entry.Hash = reader.ReadUInt(Endian.Big);
                entry.Unknown = reader.ReadUShort(Endian.Big);
                entry.Flag = (SkinFlag)reader.ReadUShort(Endian.Big);
                entry.Name = GetNameString(reader.ReadNullTerminatedString(), entry.Flag);
                uint Offset = (uint)reader.BaseStream.Position;
                var buffer = new byte[entry.DecompressedSize];
                if (entry.CompressedSize == 0)
                    entry.Data = reader.ReadBytes((int)entry.DecompressedSize);
                else
                    //entry.Data = reader.ReadBytes((int)entry.CompressedSize);
                    entry.Data = Decompress(reader, buffer, Offset, entry.CompressedSize);

                var ext = Path.GetExtension(entry.Name);
                if (decompressXML && (ext == ".xml" || ext == ".mtl" || ext == ".animevents" || ext == ".chrparams"))
                {
                    PBXML pbxml = new PBXML();
                    entry.Data = pbxml.Read(entry.Data);
                }
                var path = Directory.CreateDirectory(Path.Combine(CreateDirectory(inFile), Path.GetDirectoryName(entry.Name)));
                using var writer = new NativeWriter(new FileStream(Path.Combine(path.FullName, Path.GetFileName(entry.Name)), FileMode.Create));
                writer.Write(entry.Data);
                //using var writer = new BinaryWriter(File.OpenWrite(Path.GetDirectoryName(inFile) + Directory.CreateDirectory(Path.GetFileNameWithoutExtension(inFile)) + entry.Name));
            }
            Console.WriteLine();

        }

        private string CreateDirectory(string inFile)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(inFile));
            var directory = Path.Combine(Path.GetDirectoryName(inFile), nameWithoutExtension);
            
            return directory;
        }
        private string GetNameString(string Name, SkinFlag flag)
        {
            if (flag > SkinFlag.Default)
            {
                if (Name.Contains(".dds")) Name = Name.Replace(".dds", "_alt.dds");
                if (Name.Contains(".mtl")) Name = Name.Replace(".mtl", "_alt.mtl");
            }
            Console.WriteLine("Unpacking: " + Name);
            Name = Name.Replace("/", "\\");
            return Name; 
        }
        private byte[] Decompress(NativeReader reader, byte[] buffer, uint Offset, uint compressedSize)
        {
            using var ms = new MemoryStream(buffer, true);
            while (ms.Position < buffer.Length && reader.Position < Offset + compressedSize)
            {
                var size = reader.ReadCryIntWithFlag(out var backFlag);

                if (backFlag)
                {
                    var copyLength = reader.ReadCryInt();
                    var copyBaseOffset = (int)ms.Position - copyLength;

                    for (var remaining = size + 3; remaining > 0; remaining -= copyLength)
                        ms.Write(buffer, copyBaseOffset, Math.Min(copyLength, remaining));
                }
                else
                {
                    if (reader.Read(buffer, (int)ms.Position, size) != size)
                        throw new EndOfStreamException();
                    ms.Position += size;
                }
            }

            if (ms.Position != buffer.Length)
                throw new EndOfStreamException();

            return buffer;
        }
        public void Save(string inDirectory, ushort unkVal = 0)
        {
            string[] files = Directory.GetFiles(inDirectory, "*", SearchOption.AllDirectories);
            //uint hashtest = 0;  // Hash
            //Random rnd = new Random();
            foreach (string text in files)
            {
                string text2 = text.Replace(inDirectory, "").Replace("\\", "/").Substring(1);
                byte[] array = File.ReadAllBytes(text);
                Console.WriteLine("Packing: " + text2);
                FileEntry item = new FileEntry();
                item.CompressedSize = 0;
                item.DecompressedSize = (uint)array.Length;
                item.Hash = 0;
                item.Unknown = unkVal;
                if (text2.Contains("1_heroes/amy"))
                    item.Flag = SaveFlag(text2, SkinFlag.Amy_Default, SkinFlag.Amy_Alt);
                else if (text2.Contains("1_heroes/knuckles"))
                    item.Flag = SaveFlag(text2, SkinFlag.Knuckles_Default, SkinFlag.Knuckles_Alt);
                else if (text2.Contains("1_heroes/sonic"))
                    item.Flag = SaveFlag(text2, SkinFlag.Sonic_Default, SkinFlag.Sonic_Alt);
                else if (text2.Contains("1_heroes/tails"))
                    item.Flag = SaveFlag(text2, SkinFlag.Tails_Default, SkinFlag.Tails_Alt);
                else item.Flag = SkinFlag.Default;

                if (item.Flag < SkinFlag.Default) text2 = text2.Replace("_alt", "");
                item.Name = text2;
                item.Data = array;
                Entries.Add(item);
            }
            Save(Entries, inDirectory);
        }

        private void Save(List<FileEntry> entries, string directory)
        {
            NativeWriter writer = new NativeWriter(new FileStream(Path.Combine(Path.GetDirectoryName(directory), Path.GetFileName(directory) + ".wiiu.stream"), FileMode.Create));
            writer.Write(Signature, Endian.Big);
            foreach (FileEntry entry in entries)
            {
                writer.Write(0);
                writer.Write(entry.DecompressedSize,Endian.Big);
                writer.Write(entry.Hash, Endian.Big);
                writer.Write(entry.Unknown, Endian.Big);
                writer.Write((short)entry.Flag, Endian.Big);
                writer.WriteNullTerminatedString(entry.Name);
                writer.Write(entry.Data);
            }
            writer.Close();
        }

        public SkinFlag SaveFlag(string text2, SkinFlag DefaultSkin, SkinFlag AltSkin)
        {
            if ((text2.Contains("_alt") && text2.Contains(".dds")) ||
                (text2.Contains("_alt") && text2.Contains(".mtl")))
                return AltSkin;
            else if (text2.Contains(".cgf") || text2.Contains(".chr") || text2.Contains(".cdf") || 
                     text2.Contains(".dba") || text2.Contains(".lmg") || text2.Contains(".animevents"))
                return SkinFlag.Default;
            else return DefaultSkin;
        }
    }
}
