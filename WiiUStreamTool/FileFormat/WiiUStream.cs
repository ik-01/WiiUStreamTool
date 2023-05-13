using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.ObjectPool;
using WiiUStreamTool.Util;

namespace WiiUStreamTool.FileFormat;

public static class WiiUStream {
    public const string MetadataFilename = "_WiiUStreamMetadata.txt";
    public static readonly ImmutableArray<byte> Magic = "strm"u8.ToArray().ToImmutableArray();

    public static async Task Extract(
        Stream stream,
        string basePath,
        bool preservePbxml,
        bool overwrite,
        ExtractProgress? progress,
        CancellationToken cancellationToken) {
        using var reader = new NativeReader(stream, Encoding.UTF8, true) {IsBigEndian = true};

        if (!Magic.SequenceEqual(reader.ReadBytes(4)))
            throw new InvalidDataException("Given file does not have the correct magic value.");

        Directory.CreateDirectory(basePath);
        await using var metadata = new StreamWriter(
            Path.Combine(basePath, MetadataFilename),
            false,
            new UTF8Encoding());

        using var ms = new MemoryStream();
        using var msr = new NativeReader(ms);
        while (reader.BaseStream.Position < reader.BaseStream.Length) {
            cancellationToken.ThrowIfCancellationRequested();
            
            var fe = FileEntryHeader.FromReader(reader, basePath);
            await metadata.WriteLineAsync(fe.ToLine());

            if (!overwrite && Path.Exists(fe.LocalPath)) {
                reader.BaseStream.Position += fe.CompressedSize == 0 ? fe.DecompressedSize : fe.CompressedSize;
                progress?.Invoke(ref fe, reader.BaseStream.Position, reader.BaseStream.Length, true, true);
                continue;
            }

            progress?.Invoke(ref fe, reader.BaseStream.Position, reader.BaseStream.Length, false, false);

            ms.SetLength(fe.DecompressedSize);
            ms.Position = 0;
            DecompressOne(reader.BaseStream, ms, fe.DecompressedSize, fe.CompressedSize);

            Directory.CreateDirectory(Path.GetDirectoryName(fe.LocalPath)!);
            var tempPath = $"{fe.LocalPath}.tmp{Environment.TickCount64:X}";
            try {
                await using (var target = new FileStream(tempPath, FileMode.Create)) {
                    msr.BaseStream.Position = 0;
                    if (!preservePbxml &&
                        ms.Length >= Pbxml.Magic.Length &&
                        ms.GetBuffer().AsSpan().CommonPrefixLength(Pbxml.Magic.AsSpan()) == Pbxml.Magic.Length)
                        Pbxml.Unpack(msr, new(target, new UTF8Encoding()));
                    else
                        await target.WriteAsync(ms.GetBuffer().AsMemory(0, (int)ms.Length), cancellationToken);
                }

                if (Path.Exists(fe.LocalPath))
                    File.Replace(tempPath, fe.LocalPath, null);
                else
                    File.Move(tempPath, fe.LocalPath);
                
                progress?.Invoke(ref fe, reader.BaseStream.Position, reader.BaseStream.Length, false, true);
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

    public static async Task Compress(
        string basePath,
        Stream target,
        bool preserveXml,
        int compressionLevel,
        int compressionChunkSize,
        CompressProgress progress,
        CancellationToken cancellationToken) {
        FileEntryHeader[] files;
        using (var s = new StreamReader(File.OpenRead(Path.Combine(basePath, MetadataFilename)))) {
            var filesList = new List<FileEntryHeader>();
            while (true) {
                var l = await s.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(l))
                    break;
                filesList.Add(FileEntryHeader.FromLine(l, basePath));
            }

            files = filesList.ToArray();
        }

        await using var writer = new NativeWriter(target, Encoding.UTF8) {IsBigEndian = true};
        writer.Write(Magic.AsSpan());

        using var rawStream = new MemoryStream();
        using var compressionBuffer = new MemoryStream();
        await using var compressionBufferWriter = new BinaryWriter(compressionBuffer);
        for (var i = 0; i < files.Length; i++) {
            progress(i, files.Length, ref files[i]);

            await using (var f = new FileStream(files[i].LocalPath, FileMode.Open, FileAccess.Read)) {
                rawStream.SetLength(f.Length);
                rawStream.Position = 0;
                await f.CopyToAsync(rawStream, cancellationToken);
            }

            var raw = rawStream.GetBuffer().AsMemory(0, checked((int) rawStream.Length));

            if (!preserveXml && raw.StartsWith("<?xml"u8)) {
                compressionBuffer.SetLength(0);
                compressionBuffer.Position = 0;
                rawStream.Position = 0;
                Pbxml.Pack(rawStream, compressionBufferWriter);
                rawStream.SetLength(compressionBuffer.Length);
                rawStream.Position = 0;
                compressionBuffer.Position = 0;
                await compressionBuffer.CopyToAsync(rawStream, cancellationToken);
            }

            if (compressionLevel > 0) {
                compressionBuffer.SetLength(0);
                compressionBuffer.Position = 0;
                await CompressOne(raw, compressionBuffer, compressionLevel, compressionChunkSize, cancellationToken);
                compressionBuffer.Position = 0;
            }

            var compressedSize = compressionLevel > 0 ? checked((int) compressionBuffer.Length) : 0;
            var discardCompression = compressionLevel <= 0 || compressedSize >= raw.Length;

            files[i].CompressedSize = discardCompression ? 0 : compressedSize;
            files[i].DecompressedSize = raw.Length;
            files[i].Hash = discardCompression
                ? Crc32.Get(raw.Span)
                : Crc32.Get(compressionBuffer.GetBuffer(), 0, compressedSize);
            files[i].WriteTo(writer);
            await writer.BaseStream.WriteAsync(
                discardCompression ? raw : compressionBuffer.GetBuffer().AsMemory(0, compressedSize),
                cancellationToken);
            progress(i, files.Length, ref files[i]);
        }
    }

    public static async Task CompressOne(
        Memory<byte> raw,
        Stream target,
        int level,
        int chunkSize,
        CancellationToken cancellationToken) {

        // Is chunking disabled, or is the file small enough that there is no point in multithreading?
        if (chunkSize <= 0 || raw.Length <= chunkSize) {
            CompressChunk(raw.Span, target, level, cancellationToken);
            return;
        }
        
        var pool = ObjectPool.Create(new DefaultPooledObjectPolicy<MemoryStream>());

        Task<MemoryStream> DoChunk(int offset, int length) => Task.Run(() => {
            var ms = pool.Get();
            ms.SetLength(ms.Position = 0);
            CompressChunk(raw.Span.Slice(offset, length), ms, level, cancellationToken);
            return ms;
        }, cancellationToken);

        var concurrency = Environment.ProcessorCount;
        var tasks = new List<Task<MemoryStream>>(Math.Min((raw.Length + chunkSize - 1) / chunkSize, concurrency));

        var runningTasks = new HashSet<Task<MemoryStream>>();
        for (var i = 0; i < raw.Length; i += chunkSize) {
            if (runningTasks.Count >= concurrency) {
                await Task.WhenAny(runningTasks);
                runningTasks.RemoveWhere(x => x.IsCompleted);
            }

            while (tasks.FirstOrDefault()?.IsCompleted is true) {
                var result = tasks[0].Result;
                tasks.RemoveAt(0);
                result.Position = 0;
                await result.CopyToAsync(target, cancellationToken);
            }

            tasks.Add(DoChunk(i, Math.Min(chunkSize, raw.Length - i)));
            runningTasks.Add(tasks.Last());
        }

        foreach (var task in tasks) {
            var result = await task;
            result.Position = 0;
            await result.CopyToAsync(target, cancellationToken);
        }
    }

    public static void CompressChunk(Span<byte> source, Stream target, int level, CancellationToken cancellationToken) {
        var asisBegin = 0;
        var asisLen = 0;

        Span<int> lastByteIndex = stackalloc int[0x100];
        lastByteIndex.Fill(-1);
        var prevSameByteIndex = new int[source.Length];

        for (var i = 0; i < source.Length;) {
            cancellationToken.ThrowIfCancellationRequested();
            
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
                    target.WriteCryIntWithFlag(asisLen, false);
                    target.Write(source.Slice(asisBegin, asisLen));
                }

                target.WriteCryIntWithFlag(maxRepeatedSequenceLength - 3, true);
                target.WriteCryInt(lookbackOffset);

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
            target.WriteCryIntWithFlag(asisLen, false);
            target.Write(source.Slice(asisBegin, asisLen));
        }
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

    public delegate void ExtractProgress(
        ref FileEntryHeader fe,
        long progress,
        long max,
        bool overwriteSkipped,
        bool complete);

    public delegate void CompressProgress(int progress, int max, ref FileEntryHeader header);
}
