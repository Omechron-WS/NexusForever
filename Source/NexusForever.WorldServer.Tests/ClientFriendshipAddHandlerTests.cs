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
    public sealed class ClientFriendshipAddHandlerTests
    {
        private const ulong CharacterId = 101ul;
        private const ushort RealmId = 1;
        private const ulong TargetCharacterId = 202ul;
        private const ushort TargetRealmId = 2;

        public static TheoryData<FriendshipType> NonClientTypes => new()
        {
            (FriendshipType)2,
            FriendshipType.FriendAndRival,
            (FriendshipType)5,
            (FriendshipType)6,
            (FriendshipType)7,
            (FriendshipType)9,
            (FriendshipType)10,
            (FriendshipType)11,
            (FriendshipType)12,
            (FriendshipType)13,
            (FriendshipType)14,
            (FriendshipType)15
        };

        public static TheoryData<FriendshipType> ClientTypes => new()
        {
            FriendshipType.Friend,
            FriendshipType.Ignore,
            FriendshipType.Rival,
            FriendshipType.Account
        };

        [Theory]
        [MemberData(nameof(NonClientTypes))]
        public void NonClientType_ByNameIsRejectedBeforeSessionPlayerOrBrokerAccess(FriendshipType type)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientFriendshipAddByNameHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateNameMessage(type)));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(NonClientTypes))]
        public void NonClientType_ByIdentityIsRejectedBeforeSessionPlayerOrBrokerAccess(FriendshipType type)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientFriendshipAddByIdentityHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateIdentityMessage(type)));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ClientTypes))]
        public void ClientType_ByNameForwardsAuthenticatedIdentityAndPacketFields(FriendshipType type)
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player) = CreateSession();
            FriendshipNameInviteRequestMessage published = null;
            var publisher = CreatePublisher(message => published = message);
            var handler = new ClientFriendshipAddByNameHandler(publisher.Object);

            handler.HandleMessage(session.Object, CreateNameMessage(type));

            Assert.NotNull(published);
            Assert.Equal(CharacterId, published.Inviter.Id);
            Assert.Equal(RealmId, published.Inviter.RealmId);
            Assert.Equal("Target", published.InviteeName.Name);
            Assert.Equal("Realm", published.InviteeName.RealmName);
            Assert.Equal(type, published.Type);
            Assert.Equal("Note", published.Note);
            VerifySinglePublish(session, player, publisher);
        }

        [Theory]
        [MemberData(nameof(ClientTypes))]
        public void ClientType_ByIdentityForwardsAuthenticatedAndTargetIdentities(FriendshipType type)
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player) = CreateSession();
            FriendshipNameInviteRequestMessage published = null;
            var publisher = CreatePublisher(message => published = message);
            var handler = new ClientFriendshipAddByIdentityHandler(publisher.Object);

            handler.HandleMessage(session.Object, CreateIdentityMessage(type));

            Assert.NotNull(published);
            Assert.Equal(CharacterId, published.Inviter.Id);
            Assert.Equal(RealmId, published.Inviter.RealmId);
            Assert.Equal(TargetCharacterId, published.Invitee.Id);
            Assert.Equal(TargetRealmId, published.Invitee.RealmId);
            Assert.Equal(type, published.Type);
            Assert.Equal("Note", published.Note);
            VerifySinglePublish(session, player, publisher);
        }

        private static (Mock<IWorldSession> Session, Mock<IPlayer> Player) CreateSession()
        {
            var identity = new Identity
            {
                Id      = CharacterId,
                RealmId = RealmId
            };
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Identity).Returns(identity);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);
            return (session, player);
        }

        private static Mock<IInternalMessagePublisher> CreatePublisher(
            Action<FriendshipNameInviteRequestMessage> published)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published(Assert.IsType<FriendshipNameInviteRequestMessage>(message)))
                .Returns(Task.CompletedTask);
            return publisher;
        }

        private static void VerifySinglePublish(
            Mock<IWorldSession> session,
            Mock<IPlayer> player,
            Mock<IInternalMessagePublisher> publisher)
        {
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Identity, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        private static ClientFriendshipAddByName CreateNameMessage(FriendshipType type)
        {
            var message = new ClientFriendshipAddByName();
            SetProperty(message, nameof(ClientFriendshipAddByName.Name), "Target");
            SetProperty(message, nameof(ClientFriendshipAddByName.RealmName), "Realm");
            SetProperty(message, nameof(ClientFriendshipAddByName.Type), type);
            SetProperty(message, nameof(ClientFriendshipAddByName.Note), "Note");
            return message;
        }

        private static ClientFriendshipAddByIdentity CreateIdentityMessage(FriendshipType type)
        {
            return new ClientFriendshipAddByIdentity
            {
                Target =
                {
                    Id      = TargetCharacterId,
                    RealmId = TargetRealmId
                },
                Type = type,
                Note = "Note"
            };
        }

        private static void SetProperty<T>(object target, string name, T value)
        {
            typeof(ClientFriendshipAddByName)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(target, value);
        }
    }
}
