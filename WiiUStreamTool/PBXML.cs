using System.IO;
using System.Text;
using System.Xml;
using WiiUStreamTool;

namespace PBXMLDecoder
{
    public class PBXML
    {

        public byte[] Read(byte[] data)
        {
            using var pbreader = new NativeReader(new MemoryStream(data));

            string header = pbreader.ReadNullTerminatedString();

            if (header != "pbxml")
                throw new Exception("Unknown File Format");

            XmlDocument doc = new();

            var element = CreateNewElement(pbreader, doc);
            doc.AppendChild(element);

            byte[] result = Encoding.Default.GetBytes(doc.OuterXml);
            return result;
        }
        private static XmlElement CreateNewElement(NativeReader reader, XmlDocument doc)
        {
            var numberOfChildren = reader.ReadCryInt();
            var numberOfAttributes = reader.ReadCryInt();

            var nodeName = reader.ReadNullTerminatedString();

            var element = doc.CreateElement(nodeName);

            for (var i = 0; i < numberOfAttributes; i++)
            {
                var key = reader.ReadNullTerminatedString();
                var value = reader.ReadNullTerminatedString();
                element.SetAttribute(key, value);
            }

            element.InnerText = reader.ReadNullTerminatedString();

            for (var i = 0; i < numberOfChildren; i++)
            {
                var expectedLength = reader.ReadCryInt();
                var expectedPosition = reader.BaseStream.Position + expectedLength;
                element.AppendChild(CreateNewElement(reader, doc));
                if (expectedLength != 0 && reader.BaseStream.Position != expectedPosition)
                    throw new InvalidDataException();
            }

            return element;
        }

    }
}
