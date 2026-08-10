using System.Xml;
using NexusForever.Network.Sts.Model;

namespace NexusForever.Network.Tests.Sts
{
    public class ClientKeyDataMessageTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(129)]
        [InlineData(int.MaxValue)]
        public void Read_InvalidClientCredentialLength_ThrowsInvalidDataException(int length)
        {
            byte[] payload = CreatePayload(writer => writer.Write(length));
            var message = new ClientKeyDataMessage();

            Assert.Throws<InvalidDataException>(() => message.Read(CreateDocument(payload)));
        }

        [Fact]
        public void Read_TruncatedClientCredential_ThrowsInvalidDataException()
        {
            byte[] payload = CreatePayload(writer =>
            {
                writer.Write(2);
                writer.Write((byte)1);
            });
            var message = new ClientKeyDataMessage();

            Assert.Throws<InvalidDataException>(() => message.Read(CreateDocument(payload)));
        }

        [Fact]
        public void Read_TruncatedClientEvidence_ThrowsInvalidDataException()
        {
            byte[] payload = CreatePayload(writer =>
            {
                writer.Write(1);
                writer.Write((byte)1);
                writer.Write(2);
                writer.Write((byte)2);
            });
            var message = new ClientKeyDataMessage();

            Assert.Throws<InvalidDataException>(() => message.Read(CreateDocument(payload)));
        }

        [Fact]
        public void Read_ValidFields_ReadsWithoutTrailingData()
        {
            byte[] credential = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
            byte[] evidence = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
            byte[] payload = CreatePayload(writer =>
            {
                writer.Write(credential.Length);
                writer.Write(credential);
                writer.Write(evidence.Length);
                writer.Write(evidence);
            });
            var message = new ClientKeyDataMessage();

            message.Read(CreateDocument(payload));

            Assert.Equal(credential, message.A);
            Assert.Equal(evidence, message.M1);
        }

        private static byte[] CreatePayload(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            write(writer);
            writer.Flush();
            return stream.ToArray();
        }

        private static XmlDocument CreateDocument(byte[] payload)
        {
            var document = new XmlDocument();
            document.LoadXml($"<Request><KeyData>{Convert.ToBase64String(payload)}</KeyData></Request>");
            return document;
        }
    }
}
