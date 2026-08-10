using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Cryptography;
using NexusForever.Network.Configuration.Model;
using NexusForever.Network.Session;
using NexusForever.Network.Session.Static;
using NexusForever.Network.Sts;
using NexusForever.Network.Sts.Model;
using NexusForever.Network.Tests.Session;
using NexusForever.StsServer.Network;
using NexusForever.StsServer.Network.Message;
using NexusForever.StsServer.Network.Packet;

namespace NexusForever.Network.Tests.Sts
{
    public class StsSessionTests
    {
        [Fact]
        public void HandlePacket_InvalidState_RejectsPacketBeforeHandlerInvocation()
        {
            bool invoked = false;
            StsSession session = CreateSession(() => invoked = true, SessionState.Authenticated);
            session.State = SessionState.Connected;

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => InvokeHandlePacket(session, CreatePacket()));

            Assert.IsType<InvalidDataException>(exception.InnerException);
            Assert.False(invoked);
        }

        [Fact]
        public void HandlePacket_StateAgnosticHandler_InvokesInAnyState()
        {
            bool invoked = false;
            StsSession session = CreateSession(() => invoked = true);
            session.State = SessionState.LoginStartPending;

            InvokeHandlePacket(session, CreatePacket());

            Assert.True(invoked);
        }

        [Fact]
        public void HandlePacket_AlternateReauthenticationState_InvokesHandler()
        {
            bool invoked = false;
            StsSession session = CreateSession(() => invoked = true, SessionState.Connected, SessionState.Authenticated);
            session.State = SessionState.Authenticated;

            InvokeHandlePacket(session, CreatePacket());

            Assert.True(invoked);
        }

        [Fact]
        public void HandlePacket_DocumentTypeDefinition_RejectsPacketBeforeHandlerInvocation()
        {
            bool invoked = false;
            StsSession session = CreateSession(() => invoked = true, SessionState.Authenticated);
            session.State = SessionState.Authenticated;
            ClientStsPacket packet = CreatePacket("<!DOCTYPE Request [<!ENTITY value 'expanded'>]><Request>&value;</Request>");

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => InvokeHandlePacket(session, packet));

            Assert.IsType<System.Xml.XmlException>(exception.InnerException);
            Assert.False(invoked);
        }

        [Fact]
        public async Task EnqueueMessage_StagesExactFramesAcrossRc4KeyBoundary()
        {
            var adapter = new RecordingSocketSendAdapter();
            StsSession session = CreateSendingSession(adapter, new NetworkConfig());
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            session.EnqueueMessageOk(new TestStsMessage("key"));
            byte[] keyFrame = await adapter.NextFrameAsync();
            Assert.Equal(BuildExpectedFrame("key"), keyFrame);

            byte[] key = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
            session.InitialiseEncryption(key);
            session.EnqueueMessageOk(new TestStsMessage("first"));
            session.EnqueueMessageOk(new TestStsMessage("second"));

            var expectedEncryption = new Arc4Provider(key);
            byte[] expectedFirst = BuildExpectedFrame("first");
            expectedEncryption.Encrypt(expectedFirst);
            byte[] expectedSecond = BuildExpectedFrame("second");
            expectedEncryption.Encrypt(expectedSecond);

            Assert.Equal(expectedFirst, await adapter.NextFrameAsync());
            Assert.Equal(expectedSecond, await adapter.NextFrameAsync());

            await AbortAsync(session);
        }

        [Fact]
        public async Task Draining_RejectsNewResponseWithoutAdvancingRc4State()
        {
            var adapter = new RecordingSocketSendAdapter();
            StsSession session = CreateSendingSession(adapter, new NetworkConfig());
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            session.EnqueueMessageOk(new TestStsMessage("key"));
            await adapter.NextFrameAsync();

            byte[] key = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
            session.InitialiseEncryption(key);
            session.EnqueueMessageOk(new TestStsMessage("accepted"));
            byte[] accepted = await adapter.NextFrameAsync();

            session.DisconnectAfterPendingSends();
            session.EnqueueMessageOk(new TestStsMessage("rejected"));
            await session.OutboundSendCompletion;

            Assert.False(adapter.TryGetFrame(out _));

            FieldInfo field = typeof(StsSession).GetField("serverEncryption", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            Arc4Provider actualEncryption = Assert.IsType<Arc4Provider>(field.GetValue(session));

            var expectedEncryption = new Arc4Provider(key);
            expectedEncryption.Encrypt(new byte[accepted.Length]);
            byte[] expectedProbe = new byte[16];
            expectedEncryption.Encrypt(expectedProbe);
            byte[] actualProbe = new byte[16];
            actualEncryption.Encrypt(actualProbe);
            Assert.Equal(expectedProbe, actualProbe);

            session.Update(0d);
            Assert.Equal(DisconnectState.Complete, session.ConnectionState);
            Assert.True(session.CanDispose());
        }

        [Fact]
        public async Task InvalidProofDisconnect_DrainsErrorFrameBeforeClosing()
        {
            var adapter = new BlockingSocketSendAdapter();
            StsSession session = CreateSendingSession(adapter, new NetworkConfig());
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            session.EnqueueMessageError(new ServerErrorMessage(1));
            BlockingSendRequest error = await adapter.NextRequestAsync();
            session.DisconnectAfterPendingSends();
            session.Update(0d);

            Assert.Equal(DisconnectState.Draining, session.ConnectionState);
            Assert.False(error.CancellationToken.IsCancellationRequested);

            error.Complete(error.Buffer.Length);
            await session.OutboundSendCompletion;
            session.Update(0d);

            Assert.Equal(DisconnectState.Complete, session.ConnectionState);
            Assert.True(session.CanDispose());
        }

        [Fact]
        public async Task DrainTimeout_AbortsBlockedSendUsingUpdateDelta()
        {
            var adapter = new BlockingSocketSendAdapter();
            StsSession session = CreateSendingSession(adapter, new NetworkConfig
            {
                PendingSendDrainTimeoutSeconds = 5d
            });
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            session.EnqueueMessageError(new ServerErrorMessage(1));
            BlockingSendRequest error = await adapter.NextRequestAsync();
            session.DisconnectAfterPendingSends();

            session.Update(4.9d);
            Assert.Equal(DisconnectState.Draining, session.ConnectionState);
            Assert.False(error.CancellationToken.IsCancellationRequested);

            session.Update(0.2d);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.OutboundSendCompletion);

            Assert.Equal(DisconnectState.Complete, session.ConnectionState);
            Assert.True(error.CancellationToken.IsCancellationRequested);
            Assert.True(session.CanDispose());
        }

        [Fact]
        public async Task Draining_ClearsQueuedInputAndRejectsFurtherInput()
        {
            var adapter = new BlockingSocketSendAdapter();
            var session = new TestStsSession(Mock.Of<IMessageManager>(), Options.Create(new NetworkConfig()), adapter);
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);

            byte[] request = Encoding.UTF8.GetBytes("POST /Test STS/1.0\r\nl:0\r\n\r\n");
            session.Receive(request);
            Assert.NotEmpty(GetIncomingPackets(session));

            session.EnqueueMessageError(new ServerErrorMessage(1));
            BlockingSendRequest error = await adapter.NextRequestAsync();
            session.DisconnectAfterPendingSends();

            Assert.Empty(GetIncomingPackets(session));
            session.Receive(request);
            Assert.Empty(GetIncomingPackets(session));

            session.Update(5d);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.OutboundSendCompletion);
            Assert.True(error.CancellationToken.IsCancellationRequested);
        }

        [Fact]
        public async Task Draining_DoesNotSerialiseRejectedResponse()
        {
            var adapter = new RecordingSocketSendAdapter();
            StsSession session = CreateSendingSession(adapter, new NetworkConfig());
            using ConnectedSocketPair sockets = await ConnectedSocketPair.CreateAsync();
            session.OnAccept(sockets.Server);
            session.DisconnectAfterPendingSends();
            var message = new CountingStsMessage();

            session.EnqueueMessageOk(message);

            Assert.Equal(0, message.WriteCount);
            await session.OutboundSendCompletion;
            session.Update(0d);
            Assert.True(session.CanDispose());
        }

        private static StsSession CreateSession(Action handler, params SessionState[] requiredStates)
        {
            var messageManager = new Mock<IMessageManager>();
            messageManager.Setup(manager => manager.GetMessage("/Test")).Returns(Mock.Of<IReadable>());
            messageManager.Setup(manager => manager.GetMessageHandler("/Test"))
                .Returns(new MessageHandlerInfo((NetworkSession _, IReadable _) => handler(), requiredStates));
            return new StsSession(messageManager.Object, Options.Create(new NetworkConfig()));
        }

        private static StsSession CreateSendingSession(ISocketSendAdapter adapter, NetworkConfig config)
        {
            return new StsSession(Mock.Of<IMessageManager>(), Options.Create(config), adapter);
        }

        private static ClientStsPacket CreatePacket(string body = "")
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            var packet = new ClientStsPacket(Encoding.UTF8.GetBytes($"POST /Test STS/1.0\r\nl:{bodyBytes.Length}\r\n\r\n"));
            packet.SetBody(bodyBytes, (uint)bodyBytes.Length);
            return packet;
        }

        private static void InvokeHandlePacket(StsSession session, ClientStsPacket packet)
        {
            MethodInfo method = typeof(StsSession).GetMethod("HandlePacket", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(session, [packet]);
        }

        private static ConcurrentQueue<ClientStsPacket> GetIncomingPackets(StsSession session)
        {
            FieldInfo field = typeof(StsSession).GetField("incomingPackets", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsType<ConcurrentQueue<ClientStsPacket>>(field.GetValue(session));
        }

        private static byte[] BuildExpectedFrame(string value)
        {
            string body = $"<Response value=\"{value}\" />\n";
            string frame = $"STS/1.0 200  OK\r\nl:{body.Length}\r\ns:0R\r\n\r\n{body}";
            return Encoding.UTF8.GetBytes(frame);
        }

        private static async Task AbortAsync(NetworkSession session)
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

        private sealed class TestStsMessage : IWritable
        {
            private readonly string value;

            public TestStsMessage(string value)
            {
                this.value = value;
            }

            public void Write(XmlWriter writer)
            {
                writer.WriteStartElement("Response");
                writer.WriteAttributeString("value", value);
                writer.WriteEndElement();
            }
        }

        private sealed class CountingStsMessage : IWritable
        {
            public int WriteCount { get; private set; }

            public void Write(XmlWriter writer)
            {
                WriteCount++;
            }
        }

        private sealed class TestStsSession : StsSession
        {
            public TestStsSession(
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
        }
    }
}
