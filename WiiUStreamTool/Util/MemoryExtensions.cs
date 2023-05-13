using System;

namespace WiiUStreamTool.Util;

public static class MemoryExtensions {
    public static bool StartsWith<T>(this Memory<T> memory, ReadOnlySpan<T> sequence) where T : IEquatable<T> =>
        memory.Span.StartsWith(sequence);
}
