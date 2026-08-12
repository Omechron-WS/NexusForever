using NexusForever.Game;
using NexusForever.Game.Static.Group;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Group;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Shared;

namespace NexusForever.WorldServer.Network.Message.Handler.Group
{
    public class ClientGroupLootRulesChangeHandler : IMessageHandler<IWorldSession, ClientGroupLootRulesChange>
    {
        #region Dependency Injection

        private readonly IInternalMessagePublisher messagePublisher;

        public ClientGroupLootRulesChangeHandler(
            IInternalMessagePublisher messagePublisher)
        {
            this.messagePublisher = messagePublisher;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientGroupLootRulesChange groupLootRulesChange)
        {
            if (!IsValid(groupLootRulesChange.LootRulesUnderThreshold)
                || !IsValid(groupLootRulesChange.LootRulesThresholdAndOver)
                || !IsValid(groupLootRulesChange.Threshold)
                || !IsValid(groupLootRulesChange.HarvestingRule))
                throw new InvalidPacketValueException();

            messagePublisher.PublishAsync(new GroupLootRulesUpdateMessage
            {
                GroupId          = groupLootRulesChange.GroupId,
                Identity         = session.Player.Identity.ToInternalIdentity(),
                NormalRule       = groupLootRulesChange.LootRulesUnderThreshold,
                ThresholdRule    = groupLootRulesChange.LootRulesThresholdAndOver,
                ThresholdQuality = groupLootRulesChange.Threshold,
                HarvestRule      = groupLootRulesChange.HarvestingRule
            }).FireAndForgetAsync();
        }

        private static bool IsValid(LootRule rule)
        {
            return rule is LootRule.FreeForAll
                or LootRule.RoundRobin
                or LootRule.NeedBeforeGreed
                or LootRule.Master;
        }

        private static bool IsValid(LootThreshold threshold)
        {
            return threshold is LootThreshold.Inferior
                or LootThreshold.Average
                or LootThreshold.Good
                or LootThreshold.Excellent
                or LootThreshold.Superb
                or LootThreshold.Legendary
                or LootThreshold.Artifact;
        }

        private static bool IsValid(HarvestLootRule rule)
        {
            return rule is HarvestLootRule.RoundRobin
                or HarvestLootRule.FirstTagger;
        }
    }
}
