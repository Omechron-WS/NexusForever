using System.Reflection;
using System.Text;
using Moq;
using NexusForever.Network.Session;
using NexusForever.Network.Sts;
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

        private static StsSession CreateSession(Action handler, params SessionState[] requiredStates)
        {
            var messageManager = new Mock<IMessageManager>();
            messageManager.Setup(manager => manager.GetMessage("/Test")).Returns(Mock.Of<IReadable>());
            messageManager.Setup(manager => manager.GetMessageHandler("/Test"))
                .Returns(new MessageHandlerInfo((NetworkSession _, IReadable _) => handler(), requiredStates));
            return new StsSession(messageManager.Object);
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
    }
}
