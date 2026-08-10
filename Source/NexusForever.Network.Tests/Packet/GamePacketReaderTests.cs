using NexusForever.Network.Message.Model;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.Network.Tests.Packet
{
    public class GamePacketReaderTests
    {
        [Fact]
        public void ReadUInt_PastEnd_ThrowsInvalidPacketValueException()
        {
            using var reader = CreateReader([0x01, 0x02, 0x03]);

            Assert.Throws<InvalidPacketValueException>(() => reader.ReadUInt());
        }

        [Fact]
        public void ReadBytes_LengthExceedsRemaining_ThrowsBeforeReading()
        {
            using var reader = CreateReader([0x01]);

            Assert.Throws<InvalidPacketValueException>(() => reader.ReadBytes(uint.MaxValue));
            Assert.Equal(0u, reader.BytePosition);
            Assert.Equal(1u, reader.BytesRemaining);
        }

        [Fact]
        public void ClientPacked_LengthLessThanHeader_ThrowsInvalidPacketValueException()
        {
            var message = new ClientPacked();
            using var reader = CreateReader(BitConverter.GetBytes(3u));

            Assert.Throws<InvalidPacketValueException>(() => message.Read(reader));
            Assert.Null(message.Data);
            Assert.Equal(0u, reader.BytesRemaining);
        }

        [Fact]
        public void ClientEncrypted_LengthLessThanHeader_ThrowsInvalidPacketValueException()
        {
            var message = new ClientEncrypted();
            using var reader = CreateReader(BitConverter.GetBytes(3u));

            Assert.Throws<InvalidPacketValueException>(() => message.Read(reader));
            Assert.Null(message.Data);
            Assert.Equal(0u, reader.BytesRemaining);
        }

        [Fact]
        public void ClientPackedWorld_LengthLessThanHeader_ThrowsInvalidPacketValueException()
        {
            var message = new ClientPackedWorld();
            byte[] data = [0, .. BitConverter.GetBytes(3u)];
            using var reader = CreateReader(data);

            Assert.Throws<InvalidPacketValueException>(() => message.Read(reader));
            Assert.Null(message.Data);
            Assert.Equal(0u, reader.BytesRemaining);
        }

        private static GamePacketReader CreateReader(byte[] data)
        {
            return new GamePacketReader(new MemoryStream(data));
        }
    }
}
