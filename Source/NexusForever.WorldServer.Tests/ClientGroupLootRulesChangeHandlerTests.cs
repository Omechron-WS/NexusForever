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
    public sealed class ClientGroupLootRulesChangeHandlerTests
    {
        private const ulong GroupId = 42ul;
        private const ulong CharacterId = 101ul;
        private const ushort RealmId = 1;

        public static TheoryData<LootRule, LootRule, LootThreshold, HarvestLootRule> InvalidLootRuleTuples => new()
        {
            { (LootRule)4, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { (LootRule)5, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { (LootRule)6, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { (LootRule)7, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, (LootRule)4, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, (LootRule)5, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, (LootRule)6, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, (LootRule)7, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)0, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)8, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)9, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)10, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)11, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)12, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)13, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)14, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)15, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, LootThreshold.Good, (HarvestLootRule)2 },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, LootThreshold.Good, (HarvestLootRule)3 },
        };

        public static TheoryData<LootRule, LootRule, LootThreshold, HarvestLootRule> ValidLootRuleTuples => new()
        {
            { LootRule.FreeForAll, LootRule.FreeForAll, LootThreshold.Inferior, HarvestLootRule.RoundRobin },
            { LootRule.RoundRobin, LootRule.RoundRobin, LootThreshold.Average, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.RoundRobin },
            { LootRule.Master, LootRule.Master, LootThreshold.Excellent, HarvestLootRule.FirstTagger },
            { LootRule.FreeForAll, LootRule.RoundRobin, LootThreshold.Superb, HarvestLootRule.RoundRobin },
            { LootRule.RoundRobin, LootRule.NeedBeforeGreed, LootThreshold.Legendary, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.Master, LootThreshold.Artifact, HarvestLootRule.RoundRobin },
        };

        [Theory]
        [MemberData(nameof(InvalidLootRuleTuples))]
        public void ReservedWireValue_IsRejectedBeforePlayerOrBrokerAccess(
            LootRule normalRule,
            LootRule thresholdRule,
            LootThreshold thresholdQuality,
            HarvestLootRule harvestRule)
        {
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientGroupLootRulesChangeHandler(publisher.Object);

            Assert.Throws<InvalidPacketValueException>(() => handler.HandleMessage(
                session.Object,
                CreateMessage(normalRule, thresholdRule, thresholdQuality, harvestRule)));

            session.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ValidLootRuleTuples))]
        public void DefinedTuple_IsForwardedWithAuthenticatedIdentityUnchanged(
            LootRule normalRule,
            LootRule thresholdRule,
            LootThreshold thresholdQuality,
            HarvestLootRule harvestRule)
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

            GroupLootRulesUpdateMessage published = null;
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            publisher
                .Setup(value => value.PublishAsync(It.IsAny<object>()))
                .Callback<object>(message => published = Assert.IsType<GroupLootRulesUpdateMessage>(message))
                .Returns(Task.CompletedTask);
            var handler = new ClientGroupLootRulesChangeHandler(publisher.Object);

            handler.HandleMessage(
                session.Object,
                CreateMessage(normalRule, thresholdRule, thresholdQuality, harvestRule));

            Assert.NotNull(published);
            Assert.Equal(GroupId, published.GroupId);
            Assert.Equal(CharacterId, published.Identity.Id);
            Assert.Equal(RealmId, published.Identity.RealmId);
            Assert.Equal(normalRule, published.NormalRule);
            Assert.Equal(thresholdRule, published.ThresholdRule);
            Assert.Equal(thresholdQuality, published.ThresholdQuality);
            Assert.Equal(harvestRule, published.HarvestRule);
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Identity, Times.Once);
            publisher.Verify(value => value.PublishAsync(It.IsAny<object>()), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        private static ClientGroupLootRulesChange CreateMessage(
            LootRule normalRule,
            LootRule thresholdRule,
            LootThreshold thresholdQuality,
            HarvestLootRule harvestRule)
        {
            return new ClientGroupLootRulesChange
            {
                GroupId                       = GroupId,
                LootRulesUnderThreshold       = normalRule,
                LootRulesThresholdAndOver     = thresholdRule,
                Threshold                     = thresholdQuality,
                HarvestingRule                = harvestRule,
            };
        }
    }
}
