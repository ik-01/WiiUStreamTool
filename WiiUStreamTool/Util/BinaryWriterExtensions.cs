using System;
using System.IO;

namespace WiiUStreamTool.Util;

public static class BinaryWriterExtensions {
    public static void WriteFString(this BinaryWriter writer, string str, int length) {
        var span = str.AsSpan();
        if (span.Length > length)
            throw new ArgumentOutOfRangeException(nameof(str), str, "String length exceeding length");
        writer.Write(span);
        for (var i = span.Length; i < length; i++)
            writer.Write((char) 0);
    }

    public static void WriteCString(this BinaryWriter writer, string str) {
        writer.Write(str.AsSpan());
        writer.Write((char) 0);
    }

    public static void WritePadding(this BinaryWriter writer, byte alignment, byte padWith = 0) {
        while (writer.BaseStream.Position % alignment != 0)
            writer.Write(padWith);
    }
}
