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
    public sealed class ClientGroupSetRoleHandlerTests
    {
        private const ulong GroupId = 42ul;
        private const ulong SourceId = 101ul;
        private const ulong TargetId = 202ul;
        private const ushort RealmId = 1;

        private const GroupMemberInfoFlags AllDefinedFlags = GroupMemberInfoFlags.CanInvite
            | GroupMemberInfoFlags.CanKick
            | GroupMemberInfoFlags.Disconnected
            | GroupMemberInfoFlags.Pending
            | GroupMemberInfoFlags.RoleFlags
            | GroupMemberInfoFlags.MainTank
            | GroupMemberInfoFlags.MainAssist
            | GroupMemberInfoFlags.RaidAssistant
            | GroupMemberInfoFlags.Ready
            | GroupMemberInfoFlags.RoleLocked
            | GroupMemberInfoFlags.CanMark
            | GroupMemberInfoFlags.HasSetReady;

        public static TheoryData<GroupMemberInfoFlags> ValidChangedFlags => new()
        {
            GroupMemberInfoFlags.None,
            GroupMemberInfoFlags.CanInvite,
            GroupMemberInfoFlags.CanKick,
            GroupMemberInfoFlags.Tank,
            GroupMemberInfoFlags.Healer,
            GroupMemberInfoFlags.DPS,
            GroupMemberInfoFlags.MainTank,
            GroupMemberInfoFlags.MainAssist,
            GroupMemberInfoFlags.RaidAssistant,
            GroupMemberInfoFlags.Ready,
            GroupMemberInfoFlags.RoleLocked,
            GroupMemberInfoFlags.CanMark,
            GroupMemberInfoFlags.HasSetReady,
            GroupMemberInfoFlags.Ready | GroupMemberInfoFlags.HasSetReady,
        };

        public static TheoryData<GroupMemberInfoFlags, GroupMemberInfoFlags> InvalidChanges => new()
        {
            { (GroupMemberInfoFlags)1u, GroupMemberInfoFlags.None },
            { (GroupMemberInfoFlags)(1u << 15), GroupMemberInfoFlags.None },
            { (GroupMemberInfoFlags)uint.MaxValue, GroupMemberInfoFlags.None },
            { AllDefinedFlags, GroupMemberInfoFlags.Disconnected },
            { AllDefinedFlags, GroupMemberInfoFlags.Pending },
            { AllDefinedFlags, GroupMemberInfoFlags.Tank | GroupMemberInfoFlags.Healer },
            { AllDefinedFlags, GroupMemberInfoFlags.CanInvite | GroupMemberInfoFlags.CanKick },
            { AllDefinedFlags, (GroupMemberInfoFlags)(1u << 15) },
            { GroupMemberInfoFlags.Ready, GroupMemberInfoFlags.Ready | GroupMemberInfoFlags.HasSetReady },
            { GroupMemberInfoFlags.None, GroupMemberInfoFlags.HasSetReady },
            { GroupMemberInfoFlags.None, GroupMemberInfoFlags.Ready | GroupMemberInfoFlags.HasSetReady },
        };

        [Theory]
        [MemberData(nameof(InvalidChanges))]
        public void InvalidChange_IsRejectedBeforePlayerOrBrokerAccess(
            GroupMemberInfoFlags currentFlags,
            GroupMemberInfoFlags changedFlag)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientGroupSetRoleHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() => handler.HandleMessage(
                session.Object,
                CreateMessage(currentFlags, changedFlag)));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ValidChangedFlags))]
        public void ExactStockChangedFlag_IsForwardedWithFullDefinedSnapshot(
            GroupMemberInfoFlags changedFlag)
        {
            var identity = new Identity
            {
                Id      = SourceId,
                RealmId = RealmId,
            };
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Identity).Returns(identity);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);

            GroupMemberFlagUpdateMessage published = null;
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published = Assert.IsType<GroupMemberFlagUpdateMessage>(message))
                .Returns(Task.CompletedTask);
            var handler = new ClientGroupSetRoleHandler(publisher.Object);

            handler.HandleMessage(session.Object, CreateMessage(AllDefinedFlags, changedFlag));

            Assert.NotNull(published);
            Assert.Equal(GroupId, published.GroupId);
            Assert.Equal(SourceId, published.Source.Id);
            Assert.Equal(RealmId, published.Source.RealmId);
            Assert.Equal(TargetId, published.Target.Id);
            Assert.Equal(RealmId, published.Target.RealmId);
            Assert.Equal(AllDefinedFlags, published.CurrentFlags);
            Assert.Equal(changedFlag, published.ChangedFlag);
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Identity, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(GroupMemberInfoFlags.Tank)]
        [InlineData(GroupMemberInfoFlags.Ready)]
        [InlineData(GroupMemberInfoFlags.RoleLocked)]
        public void DefinedClearDirection_IsForwardedUnchanged(GroupMemberInfoFlags changedFlag)
        {
            GroupMemberFlagUpdateMessage published = null;
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published = Assert.IsType<GroupMemberFlagUpdateMessage>(message))
                .Returns(Task.CompletedTask);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Identity).Returns(new Identity { Id = SourceId, RealmId = RealmId });
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);
            var handler = new ClientGroupSetRoleHandler(publisher.Object);
            GroupMemberInfoFlags currentFlags = GroupMemberInfoFlags.Disconnected | GroupMemberInfoFlags.Pending;

            handler.HandleMessage(session.Object, CreateMessage(currentFlags, changedFlag));

            Assert.Equal(currentFlags, published.CurrentFlags);
            Assert.Equal(changedFlag, published.ChangedFlag);
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Identity, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        private static ClientGroupSetRole CreateMessage(
            GroupMemberInfoFlags currentFlags,
            GroupMemberInfoFlags changedFlag)
        {
            return new ClientGroupSetRole
            {
                GroupId = GroupId,
                TargetedPlayer = new NexusForever.Network.World.Message.Model.Shared.Identity
                {
                    Id      = TargetId,
                    RealmId = RealmId,
                },
                CurrentFlags = currentFlags,
                ChangedFlag  = changedFlag,
            };
        }
    }
}
