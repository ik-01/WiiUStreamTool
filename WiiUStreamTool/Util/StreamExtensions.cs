using System;
using System.IO;

namespace WiiUStreamTool.Util;

public static class StreamExtensions {
    public static int WriteAndHash(this Stream stream, Span<byte> data, ref uint hash) {
        stream.Write(data);
        hash = Crc32.Get(data, hash);
        return data.Length;
    }

    public static byte ReadByteOrThrow(this Stream stream) => stream.ReadByte() switch {
        -1 => throw new EndOfStreamException(),
        var r => (byte) r
    };

    public static void CopyToLength(this Stream source, Stream destination, int length) {
        Span<byte> buffer = stackalloc byte[4096];
        while (length > 0) {
            var chunk = source.Read(buffer[..Math.Min(buffer.Length, length)]);
            if (chunk == 0)
                throw new EndOfStreamException();
            destination.Write(buffer[..chunk]);
            length -= chunk;
        }
    }
}
