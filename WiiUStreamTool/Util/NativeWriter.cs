using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace WiiUStreamTool.Util;

public class NativeWriter : BinaryWriter {
    public NativeWriter(Stream inStream)
        : base(inStream) { }

    public NativeWriter(Stream inStream, Encoding encoding)
        : base(inStream, encoding) { }

    public NativeWriter(Stream inStream, Encoding encoding, bool leaveOpen)
        : base(inStream, encoding, leaveOpen) { }

    public bool IsBigEndian { get; set; }

    public override void Write(decimal value) {
        Span<byte> buffer = stackalloc byte[sizeof(decimal)];
        unsafe {
            fixed (byte* p = &buffer.GetPinnableReference()) {
                Span<int> bufferInts = new(p, buffer.Length / sizeof(int));
                decimal.GetBits(value, bufferInts);
            }
        }

        if (IsBigEndian == BitConverter.IsLittleEndian) {
            for (var i = 0; i < buffer.Length; i += sizeof(int))
                buffer.Slice(i, i + sizeof(int)).Reverse();
        }

        OutStream.Write(buffer);
    }

    public override void Write(Half value) {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        if (IsBigEndian)
            BinaryPrimitives.WriteHalfBigEndian(buffer, value);
        else
            BinaryPrimitives.WriteHalfLittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(float value) {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        if (IsBigEndian)
            BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        else
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(double value) {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        if (IsBigEndian)
            BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        else
            BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(short value) {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        if (IsBigEndian)
            BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        else
            BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(ushort value) {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        if (IsBigEndian)
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        else
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(int value) {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        if (IsBigEndian)
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        else
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(uint value) {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        if (IsBigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(long value) {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        if (IsBigEndian)
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        else
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(ulong value) {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        if (IsBigEndian)
            BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        else
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }
}
