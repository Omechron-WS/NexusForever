using System.Reflection;
using Moq;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Info;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Player;
using NexusForever.Network.World.Message.Model.Info;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Info;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientPlayerInfoRequestHandlerTests
    {
        private const ulong SourceId = 101ul;
        private const ushort SourceRealmId = 1;
        private const ulong TargetId = 202ul;
        private const ushort TargetRealmId = 2;

        [Theory]
        [InlineData((PlayerInfoRequestType)6)]
        [InlineData((PlayerInfoRequestType)7)]
        [InlineData((PlayerInfoRequestType)11)]
        [InlineData((PlayerInfoRequestType)12)]
        [InlineData((PlayerInfoRequestType)13)]
        [InlineData((PlayerInfoRequestType)14)]
        [InlineData((PlayerInfoRequestType)15)]
        public void ReservedWireValue_IsRejectedBeforeSessionPlayerOrBrokerAccess(PlayerInfoRequestType type)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientPlayerInfoRequestHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage(type)));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(PlayerInfoRequestType.Default)]
        [InlineData(PlayerInfoRequestType.Social)]
        [InlineData(PlayerInfoRequestType.Friend)]
        [InlineData(PlayerInfoRequestType.Rival)]
        [InlineData(PlayerInfoRequestType.Neighbour)]
        [InlineData(PlayerInfoRequestType.Loot)]
        [InlineData(PlayerInfoRequestType.BankLog)]
        [InlineData(PlayerInfoRequestType.Maker)]
        [InlineData(PlayerInfoRequestType.Pvp)]
        public void DefinedValue_IsForwardedWithAuthenticatedSourceAndPacketTarget(PlayerInfoRequestType type)
        {
            var source = new Identity
            {
                Id      = SourceId,
                RealmId = SourceRealmId,
            };
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Identity).Returns(source);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);

            PlayerInfoRequestMessage published = null;
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published = Assert.IsType<PlayerInfoRequestMessage>(message))
                .Returns(Task.CompletedTask);
            var handler = new ClientPlayerInfoRequestHandler(publisher.Object);

            handler.HandleMessage(session.Object, CreateMessage(type));

            Assert.NotNull(published);
            Assert.Equal(SourceId, published.Source.Id);
            Assert.Equal(SourceRealmId, published.Source.RealmId);
            Assert.Equal(TargetId, published.Target.Id);
            Assert.Equal(TargetRealmId, published.Target.RealmId);
            Assert.Equal(type, published.Type);
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Identity, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        private static ClientPlayerInfoRequest CreateMessage(PlayerInfoRequestType type)
        {
            var message = new ClientPlayerInfoRequest();
            typeof(ClientPlayerInfoRequest)
                .GetProperty(nameof(ClientPlayerInfoRequest.Type), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, type);
            message.Identity.Id      = TargetId;
            message.Identity.RealmId = TargetRealmId;
            return message;
        }
    }
}
