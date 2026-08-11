using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Cryptography;
using NexusForever.Network.Configuration.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Packet;
using NexusForever.Network.Session;
using NexusForever.Network.Session.Static;
using NexusForever.Network.World.Message.Model;

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

        [Fact]
        public async Task EnqueueMessage_StagesExactWireFramesInFifoOrder()
        {
            var adapter = new RecordingSocketSendAdapter();
            TestGameSession session = CreateSendingSession(adapter, new NetworkConfig());
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            session.EnqueueMessage(new TestMessage(0xAA));
            session.EnqueueMessage(new TestMessage(0xBB));

            Assert.Equal(BuildExpectedFrame(GameMessageOpcode.ServerLogout, [0xAA]), await adapter.NextFrameAsync());
            Assert.Equal(BuildExpectedFrame(GameMessageOpcode.ServerLogout, [0xBB]), await adapter.NextFrameAsync());

            await DisconnectAsync(session);
        }

        [Fact]
        public async Task EnqueueMessageEncrypted_PreservesBuild16042WrapperFraming()
        {
            var adapter = new RecordingSocketSendAdapter();
            TestGameSession session = CreateSendingSession(adapter, new NetworkConfig());
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            Assert.True(session.TryEnqueueMessageEncrypted(new TestMessage(0x7E)));

            byte[] inner = [.. BitConverter.GetBytes((ushort)GameMessageOpcode.ServerLogout), 0x7E];
            ulong key = PacketCrypt.GetKeyFromAuthBuildAndMessage();
            byte[] encrypted = new PacketCrypt(key).Encrypt(inner, inner.Length);
            byte[] wrapper = [.. BitConverter.GetBytes(encrypted.Length + sizeof(int)), .. encrypted];
            byte[] expected = BuildExpectedFrame(GameMessageOpcode.ServerRealmEncrypted, wrapper);
            Assert.Equal(expected, await adapter.NextFrameAsync());

            await DisconnectAsync(session);
        }

        [Fact]
        public async Task EnqueueMessage_SaturatedQueueFailsClosedWithoutWaitingForSend()
        {
            var adapter = new BlockingSocketSendAdapter();
            TestGameSession session = CreateSendingSession(adapter, new NetworkConfig
            {
                MaximumPendingSendBytes  = 7,
                MaximumPendingSendFrames = 1
            });
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            session.EnqueueMessage(new TestMessage(0xAA));
            BlockingSendRequest request = await adapter.NextRequestAsync();
            session.EnqueueMessage(new TestMessage(0xBB));

            Assert.False(session.CanProcessOutgoingPackets);
            Assert.Equal(DisconnectState.Pending, session.ConnectionState);
            Assert.False(request.CancellationToken.IsCancellationRequested);

            session.Update(0d);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.OutboundSendCompletion);
            Assert.True(request.CancellationToken.IsCancellationRequested);
            Assert.True(session.CanDispose());
        }

        [Fact]
        public async Task TryEnqueueMessageEncrypted_SaturatedQueueReturnsFalse()
        {
            var adapter = new BlockingSocketSendAdapter();
            TestGameSession session = CreateSendingSession(adapter, new NetworkConfig
            {
                MaximumPendingSendBytes  = 64,
                MaximumPendingSendFrames = 1
            });
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            Assert.True(session.TryEnqueueMessageEncrypted(new TestMessage(0xAA)));
            BlockingSendRequest request = await adapter.NextRequestAsync();
            Assert.False(session.TryEnqueueMessageEncrypted(new TestMessage(0xBB)));

            Assert.False(session.CanProcessOutgoingPackets);
            Assert.Equal(DisconnectState.Pending, session.ConnectionState);
            Assert.False(request.CancellationToken.IsCancellationRequested);

            session.Update(0d);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.OutboundSendCompletion);
            Assert.True(request.CancellationToken.IsCancellationRequested);
            Assert.True(session.CanDispose());
        }

        [Fact]
        public void EnqueueMessages_OutputDisabled_DoesNotSerialiseMessages()
        {
            TestGameSession session = CreateSendingSession(new RecordingSocketSendAdapter(), new NetworkConfig());
            var plainMessage = new CountingMessage();
            var encryptedMessage = new CountingMessage();
            session.CanProcessOutgoingPackets = false;

            session.EnqueueMessage(plainMessage);
            session.EnqueueMessageEncrypted(encryptedMessage);

            Assert.Equal(0, plainMessage.WriteCount);
            Assert.Equal(0, encryptedMessage.WriteCount);
        }

        [Fact]
        public void TryEnqueueMessageEncrypted_OutputDisabledReturnsFalseWithoutSerialising()
        {
            TestGameSession session = CreateSendingSession(new RecordingSocketSendAdapter(), new NetworkConfig());
            var message = new CountingMessage();
            session.CanProcessOutgoingPackets = false;

            bool queued = session.TryEnqueueMessageEncrypted(message);

            Assert.False(queued);
            Assert.Equal(0, message.WriteCount);
        }

        [Fact]
        public async Task TryEnqueueMessageEncrypted_DisconnectingReturnsFalseWithoutSerialising()
        {
            TestGameSession session = CreateSendingSession(new RecordingSocketSendAdapter(), new NetworkConfig());
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);
            var message = new CountingMessage();
            session.ForceDisconnect();

            bool queued = session.TryEnqueueMessageEncrypted(message);

            Assert.False(queued);
            Assert.Equal(0, message.WriteCount);

            await DisconnectAsync(session);
        }

        [Fact]
        public async Task TryEnqueueMessageEncrypted_SerialisationFailureReturnsFalse()
        {
            TestGameSession session = CreateSendingSession(new RecordingSocketSendAdapter(), new NetworkConfig());
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);
            var message = new ThrowingMessage();

            Assert.False(session.TryEnqueueMessageEncrypted(message));
            Assert.Equal(1, message.WriteCount);
            Assert.True(session.CanProcessOutgoingPackets);

            await DisconnectAsync(session);
        }

        [Fact]
        public void TryEnqueueMessageEncrypted_UnknownOpcodeReturnsFalseWithoutSerialising()
        {
            TestGameSession session = CreateSendingSession(new RecordingSocketSendAdapter(), new NetworkConfig());
            var message = new UnknownMessage();

            Assert.False(session.TryEnqueueMessageEncrypted(message));
            Assert.Equal(0, message.WriteCount);
            Assert.True(session.CanProcessOutgoingPackets);
        }

        [Fact]
        public void TryEnqueueMessageEncrypted_MissingEncryptionReturnsFalseAndDisconnects()
        {
            TestGameSession session = CreateSendingSession(new RecordingSocketSendAdapter(), new NetworkConfig());

            Assert.False(session.TryEnqueueMessageEncrypted(new TestMessage(0xAA)));
            Assert.False(session.CanProcessOutgoingPackets);
            Assert.Equal(DisconnectState.Pending, session.ConnectionState);
        }

        private static ConcurrentQueue<ClientGamePacket> GetIncomingPackets(GameSession session)
        {
            FieldInfo field = typeof(GameSession).GetField("incomingPackets", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            return Assert.IsType<ConcurrentQueue<ClientGamePacket>>(field.GetValue(session));
        }

        private static TestGameSession CreateSendingSession(ISocketSendAdapter adapter, NetworkConfig config)
        {
            var messageManager = new Mock<IMessageManager>();
            messageManager.Setup(manager => manager.GetOpcode(It.IsAny<IWritable>()))
                .Returns((IWritable message) => message switch
                {
                    TestMessage          => GameMessageOpcode.ServerLogout,
                    CountingMessage      => GameMessageOpcode.ServerLogout,
                    ThrowingMessage      => GameMessageOpcode.ServerLogout,
                    ServerRealmEncrypted => GameMessageOpcode.ServerRealmEncrypted,
                    _                    => null
                });
            return new TestGameSession(messageManager.Object, Options.Create(config), adapter);
        }

        private static byte[] BuildExpectedFrame(GameMessageOpcode opcode, byte[] payload)
        {
            uint size = ServerGamePacket.HeaderSize + (uint)payload.Length;
            return [.. BitConverter.GetBytes(size), .. BitConverter.GetBytes((ushort)opcode), .. payload];
        }

        private static async Task DisconnectAsync(NetworkSession session)
        {
            session.ForceDisconnect();
            session.Update(0d);
            try
            {
                await session.OutboundSendCompletion;
            }
            catch (OperationCanceledException)
            {
            }
            Assert.True(session.CanDispose());
        }

        private sealed class TestGameSession : GameSession
        {
            public TestGameSession(IMessageManager messageManager)
                : base(messageManager, Options.Create(new NetworkConfig()))
            {
            }

            public TestGameSession(
                IMessageManager messageManager,
                IOptions<NetworkConfig> networkOptions,
                ISocketSendAdapter socketSendAdapter)
                : base(messageManager, networkOptions, socketSendAdapter)
            {
            }

            public uint Receive(byte[] data)
            {
                return OnData(data);
            }

            protected override IWritable BuildEncryptedMessage(byte[] data)
            {
                return new ServerRealmEncrypted
                {
                    Data = data
                };
            }
        }

        private sealed class TestMessage : IWritable
        {
            private readonly byte value;

            public TestMessage(byte value)
            {
                this.value = value;
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(value);
            }
        }

        private sealed class CountingMessage : IWritable
        {
            public int WriteCount { get; private set; }

            public void Write(GamePacketWriter writer)
            {
                WriteCount++;
            }
        }

        private sealed class ThrowingMessage : IWritable
        {
            public int WriteCount { get; private set; }

            public void Write(GamePacketWriter writer)
            {
                WriteCount++;
                throw new InvalidOperationException();
            }
        }

        private sealed class UnknownMessage : IWritable
        {
            public int WriteCount { get; private set; }

            public void Write(GamePacketWriter writer)
            {
                WriteCount++;
            }
        }
    }
}
