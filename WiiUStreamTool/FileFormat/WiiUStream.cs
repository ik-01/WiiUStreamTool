using System.Buffers;
using System.Text;
using WiiUStreamTool.Util;

namespace WiiUStreamTool.FileFormat;

public static class WiiUStream {
    public const string MetadataFilename = "_WiiUStreamMetadata.txt";
    public const int Magic = 0x7374726d; // 'strm'

    public static void Extract(
        Stream stream,
        string basePath,
        bool decompressXml,
        bool overwrite,
        ExtractProgress? progress) {
        using var reader = new NativeReader(stream, Encoding.UTF8, true) {IsBigEndian = true};

        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("Given file does not have the correct magic value.");

        Directory.CreateDirectory(basePath);
        using var metadata = new StreamWriter(
            Path.Combine(basePath, MetadataFilename),
            false,
            new UTF8Encoding());

        using var ms = new MemoryStream();
        using var msr = new NativeReader(ms);
        while (reader.BaseStream.Position < reader.BaseStream.Length) {
            var fe = FileEntryHeader.FromReader(reader, basePath);
            metadata.WriteLine(fe.ToLine());

            if (!overwrite && Path.Exists(fe.LocalPath)) {
                reader.BaseStream.Position += fe.CompressedSize == 0 ? fe.DecompressedSize : fe.CompressedSize;
                progress?.Invoke(ref fe, reader.BaseStream.Position, reader.BaseStream.Length, true);
                continue;
            }

            progress?.Invoke(ref fe, reader.BaseStream.Position, reader.BaseStream.Length, false);

            msr.BaseStream.SetLength(fe.DecompressedSize);
            msr.BaseStream.Position = 0;
            DecompressOne(reader.BaseStream, msr.BaseStream, fe.DecompressedSize, fe.CompressedSize);

            Directory.CreateDirectory(Path.GetDirectoryName(fe.LocalPath)!);
            var tempPath = $"{fe.LocalPath}.tmp{Environment.TickCount64:X}";
            try {
                using (var target = new FileStream(tempPath, FileMode.Create)) {
                    msr.BaseStream.Position = 0;
                    if (decompressXml &&
                        ms.Length >= Pbxml.Magic.Length &&
                        ms.GetBuffer().AsSpan().CommonPrefixLength(Pbxml.Magic.AsSpan()) == Pbxml.Magic.Length)
                        Pbxml.Read(msr, new(target, new UTF8Encoding()));
                    else
                        msr.BaseStream.CopyTo(target);
                }

                if (Path.Exists(fe.LocalPath))
                    File.Replace(tempPath, fe.LocalPath, null);
                else
                    File.Move(tempPath, fe.LocalPath);
            } catch (Exception) {
                try {
                    File.Delete(tempPath);
                } catch (Exception) {
                    // swallow
                }

                throw;
            }
        }
    }

    public static void Compress(
        string basePath,
        Stream target,
        bool packXml,
        int compressionLevel,
        CompressProgress progress) {
        FileEntryHeader[] files;
        using (var s = new StreamReader(File.OpenRead(Path.Combine(basePath, MetadataFilename)))) {
            var filesList = new List<FileEntryHeader>();
            while (true) {
                var l = s.ReadLine();
                if (string.IsNullOrEmpty(l))
                    break;
                filesList.Add(FileEntryHeader.FromLine(l, basePath));
            }

            files = filesList.ToArray();
        }

        using var writer = new NativeWriter(target, Encoding.UTF8) {IsBigEndian = true};
        writer.Write(Magic);

        using var sourceBuffer = new MemoryStream();
        using var compressionBuffer = new MemoryStream();
        using var compressionBufferWriter = new BinaryWriter(compressionBuffer);
        for (var i = 0; i < files.Length; i++) {
            progress(files[i].InnerPath, i, files.Length);

            using (var f = new FileStream(files[i].LocalPath, FileMode.Open, FileAccess.Read)) {
                sourceBuffer.SetLength(f.Length);
                sourceBuffer.Position = 0;
                f.CopyTo(sourceBuffer);
            }

            var sourceSpan = sourceBuffer.GetBuffer().AsSpan(0, (int) sourceBuffer.Length);

            if (packXml &&
                sourceSpan.Length >= 5 &&
                sourceSpan[0] == '<' &&
                sourceSpan[1] == '?' &&
                sourceSpan[2] == 'x' &&
                sourceSpan[3] == 'm' &&
                sourceSpan[4] == 'l') {
                compressionBuffer.SetLength(0);
                compressionBuffer.Position = 0;
                sourceBuffer.Position = 0;
                Pbxml.Write(sourceBuffer, compressionBufferWriter);
                sourceBuffer.SetLength(compressionBuffer.Length);
                sourceBuffer.Position = 0;
                compressionBuffer.Position = 0;
                compressionBuffer.CopyTo(sourceBuffer);
            }

            var compressedSize = 0;
            var crc32 = 0u;
            if (compressionLevel > 0) {
                compressionBuffer.SetLength(0);
                compressionBuffer.Position = 0;
                CompressOne(sourceSpan, compressionBuffer, compressionLevel, out compressedSize, out crc32);
                compressionBuffer.Position = 0;
            }

            var discardCompression = compressionLevel <= 0 || compressedSize >= sourceSpan.Length;

            files[i].CompressedSize = discardCompression ? 0 : compressedSize;
            files[i].DecompressedSize = sourceSpan.Length;
            files[i].Hash = discardCompression ? Crc32.Get(sourceSpan) : crc32;
            files[i].WriteTo(writer);
            if (discardCompression)
                writer.Write(sourceSpan);
            else
                compressionBuffer.CopyTo(writer.BaseStream);
        }
    }

    public static void CompressOne(Span<byte> source, Stream target, int level, out int written, out uint crc32) {
        var asisBegin = 0;
        var asisLen = 0;

        var totalWritten = 0;
        var hash = Crc32.Get(Span<byte>.Empty);
        var cryIntBuf = ArrayPool<byte>.Shared.Rent(5);
        try {
            Span<int> lastByteIndex = stackalloc int[0x100];
            lastByteIndex.Fill(-1);
            var prevSameByteIndex = new int[source.Length];

            for (var i = 0; i < source.Length;) {
                var lookbackOffset = 1;
                var maxRepeatedSequenceLength = 0;

                for (int index = lastByteIndex[source[i]], remaining = level;
                     index != -1 && remaining > 0;
                     index = prevSameByteIndex[index], remaining--) {
                    var lookbackLength = i - index;
                    var compareTo = source.Slice(index, lookbackLength);

                    var repeatedSequenceLength = 0;
                    for (var s = source[i..]; !s.IsEmpty; s = s[lookbackLength..]) {
                        var len = compareTo.CommonPrefixLength(s);
                        repeatedSequenceLength += len;
                        if (len < lookbackLength)
                            break;
                    }

                    if (repeatedSequenceLength >= maxRepeatedSequenceLength) {
                        maxRepeatedSequenceLength = repeatedSequenceLength;
                        lookbackOffset = lookbackLength;
                    }
                }

                if (maxRepeatedSequenceLength >= 3 &&
                    maxRepeatedSequenceLength >=
                    (asisLen == 0 ? 0 : CryBinaryPrimitives.CountCryIntBytes(asisLen, false)) +
                    CryBinaryPrimitives.CountCryIntBytes(maxRepeatedSequenceLength - 3, true) +
                    CryBinaryPrimitives.CountCryIntBytes(lookbackOffset, false)) {
                    if (asisLen != 0) {
                        totalWritten += target.WriteAndHash(
                            cryIntBuf.AsSpan().WriteCryIntWithFlag(asisLen, false),
                            ref hash);
                        totalWritten += target.WriteAndHash(
                            source.Slice(asisBegin, asisLen),
                            ref hash);
                    }

                    totalWritten += target.WriteAndHash(
                        cryIntBuf.AsSpan().WriteCryIntWithFlag(maxRepeatedSequenceLength - 3, true),
                        ref hash);
                    totalWritten += target.WriteAndHash(
                        cryIntBuf.AsSpan().WriteCryInt(lookbackOffset),
                        ref hash);

                    while (maxRepeatedSequenceLength-- > 0) {
                        prevSameByteIndex[i] = lastByteIndex[source[i]];
                        lastByteIndex[source[i]] = i;

                        i++;
                    }

                    asisBegin = i;
                    asisLen = 0;
                } else {
                    prevSameByteIndex[i] = lastByteIndex[source[i]];
                    lastByteIndex[source[i]] = i;

                    asisLen++;
                    i++;
                }
            }

            if (asisLen != 0) {
                totalWritten += target.WriteAndHash(cryIntBuf.AsSpan().WriteCryIntWithFlag(asisLen, false), ref hash);
                totalWritten += target.WriteAndHash(source.Slice(asisBegin, asisLen), ref hash);
            }
        } finally {
            ArrayPool<byte>.Shared.Return(cryIntBuf);
        }

        crc32 = hash;
        written = totalWritten;
    }

    public static void DecompressOne(Stream source, Stream target, int decompressedSize, int compressedSize) {
        if (compressedSize == 0) {
            source.CopyToLength(target, decompressedSize);
        } else {
            var endOffset = source.Position + compressedSize;
            var buffer = new byte[decompressedSize];
            using var ms = new MemoryStream(buffer, true);
            while (ms.Position < buffer.Length && source.Position < endOffset) {
                var size = source.ReadCryIntWithFlag(out var backFlag);

                if (backFlag) {
                    var copyLength = source.ReadCryInt();
                    var copyBaseOffset = (int) ms.Position - copyLength;

                    for (var remaining = size + 3; remaining > 0; remaining -= copyLength)
                        ms.Write(buffer, copyBaseOffset, Math.Min(copyLength, remaining));
                } else {
                    if (source.Read(buffer, (int) ms.Position, size) != size)
                        throw new EndOfStreamException();
                    ms.Position += size;
                }
            }

            if (ms.Position != buffer.Length)
                throw new EndOfStreamException();
            target.Write(buffer);
        }
    }

    public struct FileEntryHeader {
        public int CompressedSize;
        public int DecompressedSize;
        public uint Hash;
        public ushort Unknown;
        public SkinFlag SkinFlag;
        public string InnerPath;
        public string LocalPath;

        public void WriteTo(BinaryWriter writer) {
            writer.Write(CompressedSize);
            writer.Write(DecompressedSize);
            writer.Write(Hash);
            writer.Write(Unknown);
            writer.Write((ushort) SkinFlag);
            writer.WriteCString(InnerPath);
        }

        public string ToLine() => $"{InnerPath};{SkinFlag};{Unknown}";

        public static FileEntryHeader FromLine(string line, string basePath) {
            var s = line.Split(";");
            var fe = new FileEntryHeader {
                Unknown = ushort.Parse(s[2].Trim()),
                SkinFlag = Enum.Parse<SkinFlag>(s[1].Trim()),
                InnerPath = s[0].Trim(),
            };

            fe.LocalPath = Path.Combine(basePath, fe.SkinFlag.TransformPath(fe.InnerPath).Replace("..", "__"));
            return fe;
        }

        public static FileEntryHeader FromReader(BinaryReader reader, string basePath) {
            var fe = new FileEntryHeader {
                CompressedSize = reader.ReadInt32(),
                DecompressedSize = reader.ReadInt32(),
                Hash = reader.ReadUInt32(),
                Unknown = reader.ReadUInt16(),
                SkinFlag = (SkinFlag) reader.ReadUInt16(),
                InnerPath = reader.ReadCString(),
            };

            fe.LocalPath = Path.Combine(basePath, fe.SkinFlag.TransformPath(fe.InnerPath).Replace("..", "__"));
            return fe;
        }
    }

    public delegate void ExtractProgress(ref FileEntryHeader fe, long progress, long max, bool overwriteSkipped);

    public delegate void CompressProgress(string path, int progress, int max);
}
