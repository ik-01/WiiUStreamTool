using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace WiiUStreamTool.Util;

public class NativeReader : BinaryReader {
    public NativeReader(Stream input)
        : base(input) { }

    public NativeReader(Stream input, Encoding encoding)
        : base(input, encoding) { }

    public NativeReader(Stream input, Encoding encoding, bool leaveOpen)
        : base(input, encoding, leaveOpen) { }

    public bool IsBigEndian { get; set; }

    public override decimal ReadDecimal() {
        Span<byte> buffer = stackalloc byte[sizeof(decimal)];
        BaseStream.ReadExactly(buffer);

        if (IsBigEndian == BitConverter.IsLittleEndian) {
            for (var i = 0; i < buffer.Length; i += sizeof(int))
                buffer.Slice(i, i + sizeof(int)).Reverse();
        }

        unsafe {
            fixed (byte* p = &buffer.GetPinnableReference()) {
                Span<int> bufferInts = new(p, buffer.Length);
                return new(bufferInts);
            }
        }
    }

    public override Half ReadHalf() {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadHalfBigEndian(buffer)
            : BinaryPrimitives.ReadHalfLittleEndian(buffer);
    }

    public override float ReadSingle() {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadSingleBigEndian(buffer)
            : BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    public override double ReadDouble() {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadDoubleBigEndian(buffer)
            : BinaryPrimitives.ReadDoubleLittleEndian(buffer);
    }

    public override short ReadInt16() {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadInt16BigEndian(buffer)
            : BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }

    public override ushort ReadUInt16() {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(buffer)
            : BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    public override int ReadInt32() {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadInt32BigEndian(buffer)
            : BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public override uint ReadUInt32() {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(buffer)
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public override long ReadInt64() {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadInt64BigEndian(buffer)
            : BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    public override ulong ReadUInt64() {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BaseStream.ReadExactly(buffer);
        return IsBigEndian
            ? BinaryPrimitives.ReadUInt64BigEndian(buffer)
            : BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }
}
