using WiiUStreamTool;
internal class Program
{
    static WiiUStream wiiustream = new();
    private static void Main(string[] args)
    {
        if (args.Length != 0)
        {
            bool decompressXML = false;
            string? file;
            if (args[0] == "-pbxml")
            {
                file = args[1];
                decompressXML = true;
            }
            else file = args[0];
            if (File.Exists(file))
            {
                wiiustream.Read(file, decompressXML);
            }
            else if (Directory.Exists(file))
            {
               
                wiiustream.Save(file, ushort.Parse(args[1]));
            }
                
        }
        else
        {
            Console.WriteLine("WiiUStreamTool - A tool for extracting and repacking wiiu.stream archives\n" +
                              "Created by ik-01\n" +
                              "C# decompression code by srkizer\n\n" +
                              "WARNING: this tool does not compress data!\n\n" +
                              "Usage:\n" +
                              "Extract:\t\t\tWiiUStreamTool.exe archive.wiiu.stream\n" +
                              "Extract and decompress xmls:\tWiiUStreamTool.exe -pbxml archive.wiiu.stream\n" +
                              "Repack files:\t\t\tWiiUStreamTool.exe \"C:\\Directory\"");
            Console.ReadKey();
        }
       
    }
}
