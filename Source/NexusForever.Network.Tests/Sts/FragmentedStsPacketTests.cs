using System.Text;
using NexusForever.StsServer.Network.Packet;

namespace NexusForever.Network.Tests.Sts
{
    public class FragmentedStsPacketTests
    {
        [Fact]
        public void Populate_HeaderExceedsMaximumLength_ThrowsInvalidDataException()
        {
            var fragmentedPacket = new FragmentedStsPacket();
            using var reader = CreateReader(new byte[8193]);

            Assert.Throws<InvalidDataException>(() => fragmentedPacket.Populate(reader));
        }

        [Fact]
        public void Populate_HeaderAtMaximumLength_IsAccepted()
        {
            const int maximumHeaderLength = 8192;
            const string prefix = "POST /Sts/Ping STS/1.0\r\nx:";
            const string suffix = "\r\nl:0\r\n\r\n";
            string padding = new('a', maximumHeaderLength - prefix.Length - suffix.Length);
            byte[] header = Encoding.UTF8.GetBytes(prefix + padding + suffix);
            var fragmentedPacket = new FragmentedStsPacket();
            using var reader = CreateReader(header);

            Assert.Equal(maximumHeaderLength, header.Length);
            fragmentedPacket.Populate(reader);

            Assert.True(fragmentedPacket.HasHeader);
            Assert.True(fragmentedPacket.HasBody);
            Assert.Equal(string.Empty, fragmentedPacket.GetPacket().Body);
        }

        [Fact]
        public void Populate_BodyExceedsMaximumLength_ThrowsInvalidDataException()
        {
            var fragmentedPacket = new FragmentedStsPacket();
            byte[] header = Encoding.UTF8.GetBytes("POST /Auth/LoginStart STS/1.0\r\nl:65536\r\n\r\n");
            using var reader = CreateReader(header);

            Assert.Throws<InvalidDataException>(() => fragmentedPacket.Populate(reader));
        }

        [Fact]
        public void Populate_FragmentedBody_ReassemblesBody()
        {
            const string body = "fragmented-body";
            byte[] header = CreateHeader(body.Length);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            byte[] firstFragment = [.. header, .. bodyBytes[..4]];
            byte[] secondFragment = bodyBytes[4..];
            var fragmentedPacket = new FragmentedStsPacket();

            using (BinaryReader reader = CreateReader(firstFragment))
                fragmentedPacket.Populate(reader);

            Assert.True(fragmentedPacket.HasHeader);
            Assert.False(fragmentedPacket.HasBody);

            using (BinaryReader reader = CreateReader(secondFragment))
                fragmentedPacket.Populate(reader);

            Assert.True(fragmentedPacket.HasBody);
            Assert.Equal(body, fragmentedPacket.GetPacket().Body);
        }

        [Fact]
        public void Populate_CompletePacketFollowedByBytes_PreservesFollowingBytes()
        {
            byte[] firstPacket = CreatePacket("first");
            byte[] followingPacket = CreatePacket("second");
            byte[] combinedPackets = [.. firstPacket, .. followingPacket];
            var fragmentedPacket = new FragmentedStsPacket();
            using var reader = CreateReader(combinedPackets);

            fragmentedPacket.Populate(reader);

            Assert.True(fragmentedPacket.HasBody);
            Assert.Equal("first", fragmentedPacket.GetPacket().Body);
            Assert.Equal(firstPacket.Length, reader.BaseStream.Position);
            Assert.Equal(followingPacket, reader.ReadBytes(followingPacket.Length));
        }

        private static byte[] CreateHeader(int bodyLength)
        {
            return Encoding.UTF8.GetBytes($"POST /Auth/LoginStart STS/1.0\r\nl:{bodyLength}\r\n\r\n");
        }

        private static byte[] CreatePacket(string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            return [.. CreateHeader(bodyBytes.Length), .. bodyBytes];
        }

        private static BinaryReader CreateReader(byte[] data)
        {
            return new BinaryReader(new MemoryStream(data));
        }
    }
}
