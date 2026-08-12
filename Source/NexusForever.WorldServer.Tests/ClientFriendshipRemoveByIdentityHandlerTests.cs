using System.Reflection;
using Moq;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Friendship;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Friendship;
using NexusForever.Network.World.Message.Model.Friendship;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Friendship;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientFriendshipRemoveByIdentityHandlerTests
    {
        private const ulong CharacterId = 101ul;
        private const ushort RealmId = 1;
        private const ulong TargetCharacterId = 202ul;
        private const ushort TargetRealmId = 2;

        [Theory]
        [InlineData((FriendshipType)2)]
        [InlineData((FriendshipType)4)]
        [InlineData((FriendshipType)5)]
        [InlineData((FriendshipType)6)]
        [InlineData((FriendshipType)7)]
        [InlineData((FriendshipType)8)]
        [InlineData((FriendshipType)9)]
        [InlineData((FriendshipType)10)]
        [InlineData((FriendshipType)11)]
        [InlineData((FriendshipType)12)]
        [InlineData((FriendshipType)13)]
        [InlineData((FriendshipType)14)]
        [InlineData((FriendshipType)15)]
        public void NonClientRemovalType_IsRejectedBeforeSessionPlayerOrBrokerAccess(FriendshipType type)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientFriendshipRemoveByIdentityHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage(type)));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(FriendshipType.Friend)]
        [InlineData(FriendshipType.Ignore)]
        [InlineData(FriendshipType.Rival)]
        public void ClientRemovalType_IsForwardedWithAuthenticatedAndTargetIdentitiesUnchanged(FriendshipType type)
        {
            var identity = new Identity
            {
                Id      = CharacterId,
                RealmId = RealmId,
            };
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Identity).Returns(identity);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);

            FriendshipRemoveIdentityMessage published = null;
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published = Assert.IsType<FriendshipRemoveIdentityMessage>(message))
                .Returns(Task.CompletedTask);
            var handler = new ClientFriendshipRemoveByIdentityHandler(publisher.Object);

            handler.HandleMessage(session.Object, CreateMessage(type));

            Assert.NotNull(published);
            Assert.Equal(CharacterId, published.Inviter.Id);
            Assert.Equal(RealmId, published.Inviter.RealmId);
            Assert.Equal(TargetCharacterId, published.Invitee.Id);
            Assert.Equal(TargetRealmId, published.Invitee.RealmId);
            Assert.Equal(type, published.Type);
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Identity, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        private static ClientFriendshipRemoveByIdentity CreateMessage(FriendshipType type)
        {
            var message = new ClientFriendshipRemoveByIdentity();
            message.PlayerIdentity.Id = TargetCharacterId;
            message.PlayerIdentity.RealmId = TargetRealmId;
            typeof(ClientFriendshipRemoveByIdentity)
                .GetProperty(nameof(ClientFriendshipRemoveByIdentity.Type), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, type);
            return message;
        }
    }
}
