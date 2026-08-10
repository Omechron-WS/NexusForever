using System.Xml;

namespace NexusForever.Network.Sts.Model
{
    [Message("/Auth/KeyData")]
    public class ClientKeyDataMessage : IReadable
    {
        private const int MaximumClientCredentialLength = 128;
        private const int MaximumClientEvidenceLength = 32;

        public byte[] A { get; private set; }
        public byte[] M1 { get; private set; }

        /// <summary>
        /// Read and validate the client's bounded SRP credential and evidence fields.
        /// </summary>
        public void Read(XmlDocument document)
        {
            XmlNode rootNode = document["Request"];
            string keyData = rootNode["KeyData"].GetValue<string>();

            using var stream = new MemoryStream(Convert.FromBase64String(keyData));
            using var reader = new BinaryReader(stream);
            A = ReadField(reader, "client credential", MaximumClientCredentialLength);
            M1 = ReadField(reader, "client evidence", MaximumClientEvidenceLength);

            if (stream.Position != stream.Length)
                throw new InvalidDataException("STS key data contains unexpected trailing bytes.");
        }

        private static byte[] ReadField(BinaryReader reader, string name, int maximumLength)
        {
            if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(int))
                throw new InvalidDataException($"STS key data is missing the {name} length.");

            int length = reader.ReadInt32();
            if (length <= 0 || length > maximumLength)
                throw new InvalidDataException($"STS key data contains an invalid {name} length {length}.");

            if (length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException($"STS key data contains a truncated {name}.");

            return reader.ReadBytes(length);
        }
    }
}
