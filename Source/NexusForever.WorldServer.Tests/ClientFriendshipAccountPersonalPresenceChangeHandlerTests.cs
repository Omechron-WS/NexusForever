using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Static.Chat;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Friendship;
using NexusForever.Network.World.Message.Model.Friendship;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Friendship;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientFriendshipAccountPersonalPresenceChangeHandlerTests
    {
        private const uint AccountId = 123u;

        [Theory]
        [InlineData((AccountPresenceState)4)]
        [InlineData((AccountPresenceState)5)]
        [InlineData((AccountPresenceState)6)]
        [InlineData((AccountPresenceState)7)]
        public void ReservedWireValue_IsRejectedBeforeSessionAccountOrBrokerAccess(AccountPresenceState presence)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientFriendshipAccountPersonalPresenceChangeHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage(presence)));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(AccountPresenceState.Available)]
        [InlineData(AccountPresenceState.Away)]
        [InlineData(AccountPresenceState.Busy)]
        [InlineData(AccountPresenceState.Invisible)]
        public void DefinedValue_IsForwardedWithAuthenticatedAccountIdUnchanged(AccountPresenceState presence)
        {
            var account = new Mock<IAccount>(MockBehavior.Strict);
            account.SetupGet(value => value.Id).Returns(AccountId);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Account).Returns(account.Object);

            FriendshipAccountPresenceUpdateMessage published = null;
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published = Assert.IsType<FriendshipAccountPresenceUpdateMessage>(message))
                .Returns(Task.CompletedTask);
            var handler = new ClientFriendshipAccountPersonalPresenceChangeHandler(publisher.Object);

            handler.HandleMessage(session.Object, CreateMessage(presence));

            Assert.NotNull(published);
            Assert.Equal(AccountId, published.AccountId);
            Assert.Equal(presence, published.Presence);
            session.VerifyGet(value => value.Account, Times.Once);
            account.VerifyGet(value => value.Id, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            account.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        private static ClientFriendshipAccountPersonalPresenceChange CreateMessage(AccountPresenceState presence)
        {
            var message = new ClientFriendshipAccountPersonalPresenceChange();
            typeof(ClientFriendshipAccountPersonalPresenceChange)
                .GetProperty(nameof(ClientFriendshipAccountPersonalPresenceChange.Presence), BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(message, presence);
            return message;
        }
    }
}
