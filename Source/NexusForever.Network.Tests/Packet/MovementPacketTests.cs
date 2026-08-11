using NexusForever.Game.Static.Entity.Movement;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Entity;

namespace NexusForever.Network.Tests.Packet
{
    public class MovementPacketTests
    {
        [Fact]
        public void Build16042DashOpcode_IsStable()
        {
            Assert.Equal(0x00DE, (ushort)GameMessageOpcode.ClientDash);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void ClientDash_ReadsExactlyThreeBitDirection(int encodedDirection)
        {
            byte[] payload = [(byte)(0b1011_1000 | encodedDirection)];
            var message = new ClientDash();

            using var reader = new GamePacketReader(new MemoryStream(payload));
            message.Read(reader);

            Assert.Equal((DashDirection)encodedDirection, message.Direction);
            Assert.Equal((byte)0b10111, reader.ReadByte(5u));
            Assert.Equal(0u, reader.BytesRemaining);
        }

        [Fact]
        public void ClientDash_EmptyPayloadIsRejected()
        {
            var message = new ClientDash();
            using var reader = new GamePacketReader(new MemoryStream([]));

            Assert.Throws<InvalidPacketValueException>(() => message.Read(reader));
        }
    }
}
