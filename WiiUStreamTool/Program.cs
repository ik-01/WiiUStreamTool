using System.CommandLine;
using WiiUStreamTool.FileFormat;

namespace WiiUStreamTool;

public static class Program {
    private static Command GetExtractCommand(Option<bool> overwriteOption) {
        var cmd = new Command("extract");
        cmd.AddAlias("e");

        var pathArgument = new Argument<string>("path", "Specify path to a .wiiu.stream archive.");
        cmd.AddArgument(pathArgument);

        var outPathOption = new Option<string?>(
            "--out-path",
            () => null,
            "Specify target directory. Defaults to filename without extension.");
        outPathOption.AddAlias("-o");
        cmd.AddOption(outPathOption);

        var textXmlOption = new Option<bool>(
            "--text-xml",
            () => false,
            "Extract packed binary XML files into text XML file.");
        textXmlOption.AddAlias("-t");
        cmd.AddOption(textXmlOption);

        cmd.SetHandler((path, outPath, textXml, overwrite) => {
            using var f = File.OpenRead(path);
            outPath ??= Path.Combine(
                Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)));
            ushort unknownValue = 0;
            WiiUStream.Extract(
                f,
                outPath,
                textXml,
                overwrite,
                (ref WiiUStream.FileEntryHeader fe, long progress, long max, bool skipped) => {
                    unknownValue = fe.Unknown;
                    Console.WriteLine(
                        "[{0:00.00}%] {1} ({2:##,###} bytes){3}",
                        100.0 * progress / max,
                        fe.InnerPath,
                        fe.DecompressedSize,
                        skipped ? " [SKIPPED]" : "");
                });
            Console.WriteLine("Done! Unknown value is {0}.", unknownValue);
            return Task.FromResult(0);
        }, pathArgument, outPathOption, textXmlOption, overwriteOption);

        return cmd;
    }

    private static Command GetCompressCommand(Option<bool> overwriteOption) {
        var cmd = new Command("compress");
        cmd.AddAlias("c");

        var pathArgument = new Argument<string>("path", "Specify path to a folder to compress.");
        cmd.AddArgument(pathArgument);

        var outPathOption = new Option<string?>(
            "--out-path",
            () => null,
            "Specify target path. Defaults to given folder name with .wiiu.stream extension.");
        outPathOption.AddAlias("-o");
        cmd.AddOption(outPathOption);

        var packXmlOption = new Option<bool>(
            "--pack-xml",
            () => false,
            "Pack XML files into packed binary XML file.");
        packXmlOption.AddAlias("-p");
        cmd.AddOption(packXmlOption);

        var compressionLevelOption = new Option<int>(
            "--compression-level",
            () => 8,
            "Specify the effort for compressing files. Use 0 to disable compression. Time taken scales linearly.");
        compressionLevelOption.AddAlias("-l");
        cmd.AddOption(compressionLevelOption);

        cmd.SetHandler((path, outPath, packXml, compressionLevel, overwrite) => {
            outPath ??= Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".wiiu.stream");
            if (!overwrite && Path.Exists(outPath)) {
                Console.Error.WriteLine("File {0} already exists; aborting. Use -y to overwrite.", outPath);
                return Task.FromResult(-1);
            }

            var tmpPath = $"{outPath}.tmp{Environment.TickCount64:X}";
            try {
                using (var stream = new FileStream(tmpPath, FileMode.Create))
                    WiiUStream.Compress(
                        path,
                        stream,
                        packXml,
                        compressionLevel,
                        (s, progress, max) => Console.WriteLine(
                            "[{0:00.00}%] {1}",
                            100.0 * progress / max,
                            s));

                if (File.Exists(outPath))
                    File.Replace(tmpPath, outPath, null);
                else
                    File.Move(tmpPath, outPath);
                Console.WriteLine("Done!");
                return Task.FromResult(0);
            } catch (Exception) {
                try {
                    File.Delete(tmpPath);
                } catch (Exception) {
                    // swallow
                }

                throw;
            }
        }, pathArgument, outPathOption, packXmlOption, compressionLevelOption, overwriteOption);

        return cmd;
    }

    public static Task<int> Main(string[] args) {
        var cmd = new RootCommand("Extracts a .wiiu.stream archive, or compress into one.");

        var overwriteOption = new Option<bool>(
            "--overwrite",
            () => false,
            "Overwrite the target file if it exists.");
        overwriteOption.AddAlias("-y");
        cmd.AddGlobalOption(overwriteOption);
        cmd.AddCommand(GetExtractCommand(overwriteOption));
        cmd.AddCommand(GetCompressCommand(overwriteOption));
        return cmd.InvokeAsync(args);
    }
}
