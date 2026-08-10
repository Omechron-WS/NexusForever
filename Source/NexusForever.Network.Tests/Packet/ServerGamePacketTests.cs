using NexusForever.Network.Message;
using NexusForever.Network.Packet;

namespace NexusForever.Network.Tests.Packet
{
    public class ServerGamePacketTests
    {
        [Fact]
        public void Constructor_MaximumPacketSize_IsAccepted()
        {
            const int payloadLength = ushort.MaxValue - ServerGamePacket.HeaderSize;

            var packet = new ServerGamePacket(GameMessageOpcode.ServerLogout, new SizedMessage(payloadLength));

            Assert.Equal(ushort.MaxValue, packet.Size);
            Assert.Equal(payloadLength, packet.Data.Length);
        }

        [Fact]
        public void Constructor_PacketExceedsMaximumSize_ThrowsInvalidDataException()
        {
            const int payloadLength = ushort.MaxValue - ServerGamePacket.HeaderSize + 1;

            Assert.Throws<InvalidDataException>(() =>
                new ServerGamePacket(GameMessageOpcode.ServerLogout, new SizedMessage(payloadLength)));
        }

        private sealed class SizedMessage : IWritable
        {
            private readonly int payloadLength;

            public SizedMessage(int payloadLength)
            {
                this.payloadLength = payloadLength;
            }

            public void Write(GamePacketWriter writer)
            {
                writer.WriteBytes(new byte[payloadLength]);
            }
        }
    }
}
