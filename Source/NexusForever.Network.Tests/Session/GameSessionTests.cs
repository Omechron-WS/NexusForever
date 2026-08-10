using System.Collections.Concurrent;
using System.Reflection;
using Moq;
using NexusForever.Network.Message;
using NexusForever.Network.Packet;
using NexusForever.Network.Session;

namespace NexusForever.Network.Tests.Session
{
    public class GameSessionTests
    {
        [Theory]
        [InlineData(0u)]
        [InlineData(5u)]
        [InlineData(ushort.MaxValue + 1u)]
        public void ReceiveFrame_InvalidTotalSize_ThrowsInvalidDataException(uint totalSize)
        {
            var session = new TestGameSession(Mock.Of<IMessageManager>());

            Assert.Throws<InvalidDataException>(() => session.Receive(BitConverter.GetBytes(totalSize)));
        }

        [Fact]
        public void ReceiveFrame_FragmentedValidFrame_ReassemblesPacket()
        {
            var session = new TestGameSession(Mock.Of<IMessageManager>());
            byte[] size = BitConverter.GetBytes(6u);
            byte[] firstFragment = [.. size, 0x34];

            Assert.Equal(0u, session.Receive(firstFragment));
            Assert.Equal(0u, session.Receive([0x12]));

            ConcurrentQueue<ClientGamePacket> packets = GetIncomingPackets(session);
            Assert.True(packets.TryDequeue(out ClientGamePacket packet));
            Assert.Equal(new byte[] { 0x34, 0x12 }, packet.Data);
            Assert.False(packet.IsEncrypted);
            Assert.Empty(packets);
        }

        [Fact]
        public void HandlePacket_NestingAtLimit_FailsClosed()
        {
            var session = new TestGameSession(Mock.Of<IMessageManager>());
            FieldInfo field = typeof(GameSession).GetField("packetHandlingDepth", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(session, 4);

            session.HandlePacket(new ClientGamePacket
            {
                Data = []
            });

            Assert.False(session.CanProcessIncomingPackets);
            Assert.False(session.CanProcessOutgoingPackets);
        }

        private static ConcurrentQueue<ClientGamePacket> GetIncomingPackets(GameSession session)
        {
            FieldInfo field = typeof(GameSession).GetField("incomingPackets", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            return Assert.IsType<ConcurrentQueue<ClientGamePacket>>(field.GetValue(session));
        }

        private sealed class TestGameSession : GameSession
        {
            public TestGameSession(IMessageManager messageManager)
                : base(messageManager)
            {
            }

            public uint Receive(byte[] data)
            {
                return OnData(data);
            }

            protected override IWritable BuildEncryptedMessage(byte[] data)
            {
                throw new NotSupportedException();
            }
        }
    }
}
