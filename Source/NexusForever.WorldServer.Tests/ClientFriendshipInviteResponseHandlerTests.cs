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
    public sealed class ClientFriendshipInviteResponseHandlerTests
    {
        private const ulong InviteId = 42ul;
        private const ulong CharacterId = 101ul;
        private const ushort RealmId = 1;

        [Theory]
        [InlineData((FriendshipResponse)4)]
        [InlineData((FriendshipResponse)5)]
        [InlineData((FriendshipResponse)6)]
        [InlineData((FriendshipResponse)7)]
        public void ReservedWireValue_IsRejectedBeforeSessionPlayerOrBrokerAccess(FriendshipResponse response)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientFriendshipInviteResponseHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage(response)));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(FriendshipResponse.Mutual)]
        [InlineData(FriendshipResponse.Accept)]
        [InlineData(FriendshipResponse.Decline)]
        [InlineData(FriendshipResponse.Ignore)]
        public void DefinedValue_IsForwardedWithAuthenticatedIdentityUnchanged(FriendshipResponse response)
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

            FriendshipInviteResponseMessage published = null;
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published = Assert.IsType<FriendshipInviteResponseMessage>(message))
                .Returns(Task.CompletedTask);
            var handler = new ClientFriendshipInviteResponseHandler(publisher.Object);

            handler.HandleMessage(session.Object, CreateMessage(response));

            Assert.NotNull(published);
            Assert.Equal(CharacterId, published.Invitee.Id);
            Assert.Equal(RealmId, published.Invitee.RealmId);
            Assert.Equal(InviteId, published.InviteId);
            Assert.Equal(response, published.Response);
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Identity, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        private static ClientFriendshipInviteResponse CreateMessage(FriendshipResponse response)
        {
            var message = new ClientFriendshipInviteResponse();
            typeof(ClientFriendshipInviteResponse)
                .GetProperty(nameof(ClientFriendshipInviteResponse.InviteId), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, InviteId);
            typeof(ClientFriendshipInviteResponse)
                .GetProperty(nameof(ClientFriendshipInviteResponse.Response), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, response);
            return message;
        }
    }
}
