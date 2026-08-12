using Moq;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Group;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Group;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Group;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientGroupFlagsChangedHandlerTests
    {
        private const ulong GroupId = 42ul;
        private const ulong CharacterId = 101ul;
        private const ushort RealmId = 1;

        private const GroupFlags SupportedGroupFlags = GroupFlags.OpenWorld
            | GroupFlags.Raid
            | GroupFlags.JoinRequestOpen
            | GroupFlags.JoinRequestClosed
            | GroupFlags.ReferralsOpen
            | GroupFlags.ReferralsClosed;

        public static IEnumerable<object[]> InvalidFlags
        {
            get
            {
                for (var bit = 0; bit < 32; bit++)
                {
                    var flag = (GroupFlags)(1u << bit);
                    if ((flag & SupportedGroupFlags) == 0)
                        yield return [flag];
                }

                yield return [GroupFlags.JoinRequestOpen | GroupFlags.JoinRequestClosed];
                yield return [GroupFlags.ReferralsOpen | GroupFlags.ReferralsClosed];
                yield return [SupportedGroupFlags];
                yield return [(GroupFlags)uint.MaxValue];
            }
        }

        public static IEnumerable<object[]> ValidFlags
        {
            get
            {
                GroupFlags[] fixedFlags =
                [
                    GroupFlags.None,
                    GroupFlags.OpenWorld,
                    GroupFlags.Raid,
                    GroupFlags.OpenWorld | GroupFlags.Raid,
                ];
                GroupFlags[] joinRequestFlags =
                [
                    GroupFlags.None,
                    GroupFlags.JoinRequestOpen,
                    GroupFlags.JoinRequestClosed,
                ];
                GroupFlags[] referralFlags =
                [
                    GroupFlags.None,
                    GroupFlags.ReferralsOpen,
                    GroupFlags.ReferralsClosed,
                ];

                foreach (GroupFlags fixedFlag in fixedFlags)
                foreach (GroupFlags joinRequestFlag in joinRequestFlags)
                foreach (GroupFlags referralFlag in referralFlags)
                    yield return [fixedFlag | joinRequestFlag | referralFlag];
            }
        }

        [Theory]
        [MemberData(nameof(InvalidFlags))]
        public void InvalidFlags_AreRejectedBeforePlayerOrBrokerAccess(GroupFlags flags)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientGroupFlagsChangedHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() => handler.HandleMessage(
                session.Object,
                new ClientGroupFlagsChanged
                {
                    GroupId  = GroupId,
                    NewFlags = flags,
                }));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ValidFlags))]
        public void StructurallyValidFlags_AreForwardedWithAuthenticatedIdentity(GroupFlags flags)
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

            GroupFlagsUpdateMessage published = null;
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published = Assert.IsType<GroupFlagsUpdateMessage>(message))
                .Returns(Task.CompletedTask);
            var handler = new ClientGroupFlagsChangedHandler(publisher.Object);

            handler.HandleMessage(session.Object, new ClientGroupFlagsChanged
            {
                GroupId  = GroupId,
                NewFlags = flags,
            });

            Assert.NotNull(published);
            Assert.Equal(GroupId, published.GroupId);
            Assert.Equal(CharacterId, published.Identity.Id);
            Assert.Equal(RealmId, published.Identity.RealmId);
            Assert.Equal(flags, published.Flags);
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Identity, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }
    }
}
