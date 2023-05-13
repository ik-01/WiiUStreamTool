using System.IO;

namespace WiiUStreamTool.FileFormat;

public enum SkinFlag : short {
    Knuckles_Default = -4,
    Amy_Default,
    Tails_Default,
    Sonic_Default,
    Default,
    Sonic_Alt,
    Tails_Alt,
    Amy_Alt,
    Knuckles_Alt
}

public static class SkinFlagExtensions {
    public static string TransformPath(this SkinFlag flag, string path) {
        if (flag > SkinFlag.Default) {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".dds" or ".mtl") {
                var dirName = Path.GetDirectoryName(path);
                path = Path.GetFileNameWithoutExtension(path) + ".alt" + ext;
                if (dirName is not null)
                    path = Path.Combine(dirName, path);
            }
        }

        path = path.Replace('/', '\\');
        return path;
    }
}
