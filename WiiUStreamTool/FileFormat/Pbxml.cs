using System.Collections.Immutable;
using System.Xml;
using WiiUStreamTool.Util;

namespace WiiUStreamTool.FileFormat;

public static class Pbxml {
    public static readonly ImmutableArray<byte> Magic = new byte[] {
        (byte) 'p',
        (byte) 'b',
        (byte) 'x',
        (byte) 'm',
        (byte) 'l',
        0,
    }.ToImmutableArray();

    public static void Read(BinaryReader source, StreamWriter target) {
        if (!source.ReadBytes(6).SequenceEqual(Magic))
            throw new InvalidDataException("Unknown File Format");

        var doc = new XmlDocument();
        doc.AppendChild(CreateNewElement(source, doc));
        doc.Save(new XmlTextWriter(target) {
            Formatting = Formatting.Indented,
        });
    }

    public static void Write(Stream source, BinaryWriter target) {
        var doc = new XmlDocument();
        doc.Load(source);
        target.Write(Magic.AsSpan());
        WriteElement(target, doc.ChildNodes.OfType<XmlElement>().Single());
    }

    private static void WriteElement(BinaryWriter writer, XmlNode element) {
        writer.WriteCryInt(element.ChildNodes.Count);
        writer.WriteCryInt(element.Attributes?.Count ?? 0);
        writer.WriteCString(element.Name);
        foreach (var attrib in element.Attributes?.Cast<XmlAttribute>() ?? Array.Empty<XmlAttribute>()) {
            writer.WriteCString(attrib.Name);
            writer.WriteCString(attrib.Value);
        }
        
        writer.WriteCString(element.InnerText);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        foreach (var child in element.ChildNodes.Cast<XmlNode>()) {
            ms.SetLength(ms.Position = 0);
            WriteElement(bw, child);
            writer.WriteCryInt((int) ms.Length);
            ms.Position = 0;
            ms.CopyTo(writer.BaseStream);
        }
    }

    private static XmlElement CreateNewElement(BinaryReader reader, XmlDocument doc) {
        var numberOfChildren = reader.ReadCryInt();
        var numberOfAttributes = reader.ReadCryInt();

        var nodeName = reader.ReadCString();

        var element = doc.CreateElement(nodeName);

        for (var i = 0; i < numberOfAttributes; i++) {
            var key = reader.ReadCString();
            var value = reader.ReadCString();
            element.SetAttribute(key, value);
        }

        element.InnerText = reader.ReadCString();

        for (var i = 0; i < numberOfChildren; i++) {
            var expectedLength = reader.ReadCryInt();
            var expectedPosition = reader.BaseStream.Position + expectedLength;
            element.AppendChild(CreateNewElement(reader, doc));
            if (expectedLength != 0 && reader.BaseStream.Position != expectedPosition)
                throw new InvalidDataException();
        }

        return element;
    }
}
