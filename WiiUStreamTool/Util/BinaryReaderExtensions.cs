using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace WiiUStreamTool.Util;

public static class BinaryReaderExtensions {
    private static readonly ObjectPool<StringBuilder> StringBuilderPool =
        ObjectPool.Create(new StringBuilderPooledObjectPolicy());

    public static string ReadCString(this BinaryReader reader) {
        var sb = StringBuilderPool.Get();
        try {
            while (true) {
                var c = reader.ReadChar();
                if (c == 0)
                    break;
                sb.Append(c);
            }

            return sb.ToString();
        } finally {
            StringBuilderPool.Return(sb);
        }
    }
}
