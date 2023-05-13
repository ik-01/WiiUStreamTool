using System.CommandLine;
using WiiUStreamTool.FileFormat;
using WiiUStreamTool.Util;

namespace WiiUStreamTool;

public static class Program {
    private static Command GetExtractCommand(Command parentCommand, Option<bool> overwriteOption) {
        var command = new Command("extract");
        command.AddAlias("e");

        var pathArgument = new Argument<string>("path", "Specify path to a .wiiu.stream archive.");
        command.AddArgument(pathArgument);

        var outPathOption = new Option<string?>(
            "--out-path",
            () => null,
            "Specify target directory. Defaults to filename without extension.");
        outPathOption.AddAlias("-o");
        command.AddOption(outPathOption);

        var preservePbxmlOption = new Option<bool>(
            "--preserve-pbxml",
            () => false,
            "Keep packed binary XML files as-is.");
        preservePbxmlOption.AddAlias("-p");
        command.AddOption(preservePbxmlOption);

        command.SetHandler(context => {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            var outPath = context.ParseResult.GetValueForOption(outPathOption);
            var preservePbxml = context.ParseResult.GetValueForOption(preservePbxmlOption);
            var overwrite = context.ParseResult.GetValueForOption(overwriteOption);
            
            using var f = File.OpenRead(path);
            outPath ??= Path.Combine(
                Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)));
            WiiUStream.Extract(
                f,
                outPath,
                preservePbxml,
                overwrite,
                (ref WiiUStream.FileEntryHeader fe, long progress, long max, bool skipped) => Console.WriteLine(
                    "[{0:00.00}%] {1} ({2:##,###} bytes){3}",
                    100.0 * progress / max,
                    fe.InnerPath,
                    fe.DecompressedSize,
                    skipped ? " [SKIPPED]" : ""));
            Console.WriteLine("Done!");
            return Task.FromResult(0);
        });

        parentCommand.AddCommand(command);
        return command;
    }

    private static Command GetCompressCommand(Command parentCommand, Option<bool> overwriteOption) {
        var command = new Command("compress");
        command.AddAlias("c");

        var pathArgument = new Argument<string>("path", "Specify path to a folder to compress.");
        command.AddArgument(pathArgument);

        var outPathOption = new Option<string?>(
            "--out-path",
            () => null,
            "Specify target path. Defaults to given folder name with .wiiu.stream extension.");
        outPathOption.AddAlias("-o");
        command.AddOption(outPathOption);

        var preserveXmlOption = new Option<bool>(
            "--preserve-xml",
            () => false,
            "Keep text XML files as-is.");
        preserveXmlOption.AddAlias("-p");
        command.AddOption(preserveXmlOption);

        var compressionLevelOption = new Option<int>(
            "--compression-level",
            () => 8,
            "Specify the effort for compressing files. Use 0 to disable compression. Time taken scales linearly.");
        compressionLevelOption.AddAlias("-l");
        command.AddOption(compressionLevelOption);

        command.SetHandler(context => {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            var outPath = context.ParseResult.GetValueForOption(outPathOption);
            var preserveXml = context.ParseResult.GetValueForOption(preserveXmlOption);
            var compressionLevel = context.ParseResult.GetValueForOption(compressionLevelOption);
            var overwrite = context.ParseResult.GetValueForOption(overwriteOption);
            
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
                        preserveXml,
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
        });

        parentCommand.AddCommand(command);
        return command;
    }

    public static Task<int> Main(string[] args) {
        var cmd = new RootCommand("Extracts a .wiiu.stream archive, or compress into one.");

        var overwriteOption = new Option<bool>(
            "--overwrite",
            () => false,
            "Overwrite the target file if it exists.");
        overwriteOption.AddAlias("-y");
        cmd.AddGlobalOption(overwriteOption);
        var extractCommand = GetExtractCommand(cmd, overwriteOption);
        var compressCommand = GetCompressCommand(cmd, overwriteOption);

        // Special case when we received 1 argument (excluding application name),
        // and the second parameter is an existing folder or file.
        if (args.Length == 1 && !cmd.Subcommands.Any(x => x.Aliases.Contains(args[0])) && Path.Exists(args[0])) {
            if (Directory.Exists(args[0])) {
                if (Path.Exists(Path.Combine(args[0], WiiUStream.MetadataFilename))) {
                    using (ScopedConsoleColor.Foreground(ConsoleColor.Yellow))
                        Console.WriteLine("Assuming {0} with default options.", compressCommand.Name);
                    args = new[] {compressCommand.Name, args[0]};
                }
                
            } else if (File.Exists(args[0])) {
                Span<byte> peekResult = stackalloc byte[Math.Max(WiiUStream.Magic.Length, Pbxml.Magic.Length)];
                using (var peeker = File.OpenRead(args[0]))
                    peekResult = peekResult[..peeker.Read(peekResult)];

                if (peekResult.StartsWith(WiiUStream.Magic.AsSpan())) {
                    using (ScopedConsoleColor.Foreground(ConsoleColor.Yellow))
                        Console.WriteLine("Assuming {0} with default options.", extractCommand.Name);
                    args = new[] {extractCommand.Name, args[0]};
                }
            }
        }
        
        return cmd.InvokeAsync(args);
    }
}
