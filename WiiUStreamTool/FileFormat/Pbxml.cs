using System.Collections.Immutable;
using System.Xml;
using WiiUStreamTool.Util;

namespace WiiUStreamTool.FileFormat;

public static class Pbxml {
    public static readonly ImmutableArray<byte> Magic = "pbxml\0"u8.ToArray().ToImmutableArray();

    public static void Unpack(BinaryReader source, StreamWriter target) {
        if (!source.ReadBytes(Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Unknown File Format");

        var doc = new XmlDocument();
        doc.AppendChild(UnpackElement(source, doc));
        doc.Save(new XmlTextWriter(target) {
            Formatting = Formatting.Indented,
        });
    }

    public static void Pack(Stream source, BinaryWriter target) {
        var doc = new XmlDocument();
        doc.Load(source);
        target.Write(Magic.AsSpan());
        PackElement(target, doc.ChildNodes.OfType<XmlElement>().Single());
    }

    private static XmlElement UnpackElement(BinaryReader reader, XmlDocument doc, XmlNamespaceManager? nsmgr = null) {
        nsmgr ??= new(new NameTable());
        nsmgr.PushScope();

        var numberOfChildren = reader.ReadCryInt();
        var numberOfAttributes = reader.ReadCryInt();
        var nodeName = reader.ReadCString();

        var attrs = new Dictionary<string, string>();
        for (var i = 0; i < numberOfAttributes; i++) {
            var key = reader.ReadCString();
            var value = reader.ReadCString();

            attrs.Add(key, value);
            if (key.StartsWith("xmlns:"))
                nsmgr.AddNamespace(key[6..], value);
        }

        var element = nodeName.IndexOf(':') switch {
            -1 => doc.CreateElement(nodeName),
            var r => doc.CreateElement(nodeName[..r], nodeName[(r + 1)..], nsmgr.LookupNamespace(nodeName[..r])),
        };
        
        foreach (var (key, value) in attrs) {
            var sep = key.IndexOf(':');
            if (sep == -1 || key.StartsWith("xmlns:"))
                element.SetAttribute(key, value);
            else
                element.SetAttribute(key[(sep + 1)..], nsmgr.LookupNamespace(key[..sep]), value);
        }

        var nodeText = reader.ReadCString();
        if (nodeText != "")
            element.AppendChild(doc.CreateTextNode(nodeText));

        for (var i = 0; i < numberOfChildren; i++) {
            var expectedLength = reader.ReadCryInt();
            var expectedPosition = reader.BaseStream.Position + expectedLength;
            element.AppendChild(UnpackElement(reader, doc, nsmgr));
            if (i + 1 == numberOfChildren) {
                if (expectedLength != 0)
                    throw new InvalidDataException("Last child node must not have an expectedLength.");
            } else {
                if (reader.BaseStream.Position != expectedPosition)
                    throw new InvalidDataException("Expected length does not match.");
            }
        }

        nsmgr.PopScope();
        return element;
    }

    private static void PackElement(BinaryWriter writer, XmlNode element) {
        var textElement = element.ChildNodes.OfType<XmlText>().SingleOrDefault();
        var childElements = element.ChildNodes.OfType<XmlElement>().ToArray();
        var attributes = element.Attributes?.Cast<XmlAttribute>().ToArray() ?? Array.Empty<XmlAttribute>();

        writer.WriteCryInt(childElements.Length);
        writer.WriteCryInt(attributes.Length);
        writer.WriteCString(element.Name);
        foreach (var attrib in attributes) {
            writer.WriteCString(attrib.Name);
            writer.WriteCString(attrib.Value);
        }

        writer.WriteCString(textElement?.Value ?? "");

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        for (var i = 0; i < childElements.Length; i++) {
            if (i == childElements.Length - 1) {
                writer.WriteCryInt(0);
                PackElement(writer, childElements[i]);
            } else {
                ms.SetLength(ms.Position = 0);
                PackElement(bw, childElements[i]);
                writer.WriteCryInt((int) ms.Length);
                ms.Position = 0;
                ms.CopyTo(writer.BaseStream);
            }
        }
    }
}
