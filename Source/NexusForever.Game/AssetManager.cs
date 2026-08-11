using System.Collections.Immutable;
using System.Reflection;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.World;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Quest;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reward;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Shared;

namespace NexusForever.Game
{
    public sealed class AssetManager : Singleton<AssetManager>, IAssetManager
    {
        public static ImmutableDictionary<InventoryLocation, uint> InventoryLocationCapacities { get; private set; }

        /// <summary>
        /// Id to be assigned to the next created mail.
        /// </summary>
        public ulong NextMailId => nextMailId++;

        private ulong nextMailId;

        private ImmutableDictionary<uint, ImmutableList<ItemDisplaySourceEntryEntry>> itemDisplaySourcesEntry;

        private ImmutableDictionary</*zoneId*/uint, /*tutorialId*/uint> zoneTutorials;
        private ImmutableDictionary</*creatureId*/uint, /*targetGroupIds*/ImmutableList<uint>> creatureAssociatedTargetGroups;
        private ImmutableDictionary</*questObjectiveId*/uint, /*creatureIds*/ImmutableList<uint>> questObjectiveTargets;

        private ImmutableDictionary<AccountTier, ImmutableList<RewardPropertyPremiumModifierEntry>> rewardPropertiesByTier;

        public void Initialise()
        {
            nextMailId = DatabaseManager.Instance.GetDatabase<CharacterDatabase>().GetNextMailId() + 1ul;

            CacheInventoryBagCapacities();
            CacheItemDisplaySourceEntries();
            CacheTutorials();
            CacheCreatureTargetGroups();
            CacheQuestObjectiveTargetGroups();
            CacheRewardPropertiesByTier();
        }

        private void CacheInventoryBagCapacities()
        {
            var entries = new Dictionary<InventoryLocation, uint>();
            foreach (FieldInfo field in typeof(InventoryLocation).GetFields())
            {
                foreach (InventoryLocationAttribute attribute in field.GetCustomAttributes<InventoryLocationAttribute>())
                {
                    InventoryLocation location = (InventoryLocation)field.GetValue(null);
                    entries.Add(location, attribute.DefaultCapacity);
                }
            }

            InventoryLocationCapacities = entries.ToImmutableDictionary();
        }

        private void CacheItemDisplaySourceEntries()
        {
            var entries = new Dictionary<uint, List<ItemDisplaySourceEntryEntry>>();
            foreach (ItemDisplaySourceEntryEntry entry in GameTableManager.Instance.ItemDisplaySourceEntry.Entries)
            {
                if (!entries.ContainsKey(entry.ItemSourceId))
                    entries.Add(entry.ItemSourceId, new List<ItemDisplaySourceEntryEntry>());

                entries[entry.ItemSourceId].Add(entry);
            }

            itemDisplaySourcesEntry = entries.ToImmutableDictionary(e => e.Key, e => e.Value.ToImmutableList());
        }

        private void CacheTutorials()
        {
            var zoneEntries =  ImmutableDictionary.CreateBuilder<uint, uint>();
            foreach (TutorialModel tutorial in DatabaseManager.Instance.GetDatabase<WorldDatabase>().GetTutorialTriggers())
            {
                if (tutorial.TriggerId == 0) // Don't add Tutorials with no trigger ID
                    continue;

                if (tutorial.Type == 29 && !zoneEntries.ContainsKey(tutorial.TriggerId))
                    zoneEntries.Add(tutorial.TriggerId, tutorial.Id);
            }

            zoneTutorials = zoneEntries.ToImmutable();
        }

        private void CacheCreatureTargetGroups()
        {
            TargetGroupEntry[] targetGroups = GameTableManager.Instance.TargetGroup.Entries;
            var resolver = new TargetGroupResolver(targetGroups);
            var entries = new Dictionary<uint, HashSet<uint>>();
            foreach (TargetGroupEntry entry in targetGroups.Where(IsConcreteTargetGroup))
            {
                foreach (uint creatureId in resolver.ResolveCreatureIds(entry.Id))
                {
                    if (!entries.TryGetValue(creatureId, out HashSet<uint> targetGroupIds))
                    {
                        targetGroupIds = [];
                        entries.Add(creatureId, targetGroupIds);
                    }

                    targetGroupIds.Add(entry.Id);
                }
            }

            creatureAssociatedTargetGroups = entries.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.Order().ToImmutableList());
        }

        private void CacheQuestObjectiveTargetGroups()
        {
            TargetGroupEntry[] targetGroups = GameTableManager.Instance.TargetGroup.Entries;
            var targetGroupsById = targetGroups.ToDictionary(entry => entry.Id);
            var resolver = new TargetGroupResolver(targetGroups);
            var entries = ImmutableDictionary.CreateBuilder<uint, ImmutableList<uint>>();

            foreach (QuestObjectiveEntry objective in GameTableManager.Instance.QuestObjective.Entries)
            {
                uint targetGroupId = GetQuestObjectiveTargetGroupId(objective);
                if (targetGroupId == 0u)
                    continue;

                if (!targetGroupsById.ContainsKey(targetGroupId)
                    && objective.TargetGroupIdRewardPane != 0u)
                    targetGroupId = objective.TargetGroupIdRewardPane;

                entries[objective.Id] = resolver.ResolveCreatureIds(targetGroupId);
            }

            questObjectiveTargets = entries.ToImmutable();
        }

        private static bool IsConcreteTargetGroup(TargetGroupEntry entry)
        {
            return (TargetGroupType)entry.Type is TargetGroupType.CreatureIdGroup
                or TargetGroupType.OtherTargetGroup
                or TargetGroupType.OtherTargetGroupCreatures;
        }

        private static uint GetQuestObjectiveTargetGroupId(QuestObjectiveEntry entry)
        {
            QuestObjectiveType type = (QuestObjectiveType)entry.Type;
            if (type is QuestObjectiveType.KillTargetGroups
                or QuestObjectiveType.Unknown10
                or QuestObjectiveType.ActivateTargetGroupChecklist
                or QuestObjectiveType.KillTargetGroup
                or QuestObjectiveType.TalkToTargetGroup
                or QuestObjectiveType.ActivateTargetGroup)
                return entry.Data != 0u ? entry.Data : entry.TargetGroupIdRewardPane;

            return entry.TargetGroupIdRewardPane;
        }

        private void CacheRewardPropertiesByTier()
        {
            // VIP was intended to be used in China from what I can see, you can force the VIP premium system in the client with the China game mode parameter
            // not supported as the system was unfinished
            IEnumerable<RewardPropertyPremiumModifierEntry> hybridEntries = GameTableManager.Instance
                .RewardPropertyPremiumModifier.Entries
                .Where(e => (PremiumSystem)e.PremiumSystemEnum == PremiumSystem.Hybrid)
                .ToList();

            // base reward properties are determined by current account tier and lower if fall through flag is set
            rewardPropertiesByTier = hybridEntries
                .Select(e => e.Tier)
                .Distinct()
                .ToImmutableDictionary(k => (AccountTier)k, k => hybridEntries
                    .Where(r => r.Tier == k)
                    .Concat(hybridEntries
                        .Where(r => r.Tier < k && ((RewardPropertyPremiumModiferFlags)r.Flags & RewardPropertyPremiumModiferFlags.FallThrough) != 0))
                    .ToImmutableList());
        }

        /// <summary>
        /// Returns an <see cref="ImmutableList{T}"/> containing all <see cref="ItemDisplaySourceEntryEntry"/>'s for the supplied itemSource.
        /// </summary>
        public ImmutableList<ItemDisplaySourceEntryEntry> GetItemDisplaySource(uint itemSource)
        {
            return itemDisplaySourcesEntry.TryGetValue(itemSource, out ImmutableList<ItemDisplaySourceEntryEntry> entries) ? entries : null;
        }

        /// <summary>
        /// Returns a Tutorial ID if it's found in the Zone Tutorials cache
        /// </summary>
        public uint GetTutorialIdForZone(uint zoneId)
        {
            return zoneTutorials.TryGetValue(zoneId, out uint tutorialId) ? tutorialId : 0;
        }

        /// <summary>
        /// Returns an <see cref="ImmutableList{T}"/> containing all TargetGroup ID's associated with the creatureId.
        /// </summary>
        public ImmutableList<uint> GetTargetGroupsForCreatureId(uint creatureId)
        {
            return creatureAssociatedTargetGroups.TryGetValue(creatureId, out ImmutableList<uint> entries)
                ? entries : ImmutableList<uint>.Empty;
        }

        /// <summary>
        /// Returns the concrete creature identifiers targeted by the supplied quest objective.
        /// </summary>
        public ImmutableList<uint> GetQuestObjectiveTargetIds(uint questObjectiveId)
        {
            return questObjectiveTargets.TryGetValue(questObjectiveId, out ImmutableList<uint> entries)
                ? entries : ImmutableList<uint>.Empty;
        }

        /// <summary>
        /// Returns an <see cref="ImmutableList{T}"/> containing all <see cref="RewardPropertyPremiumModifierEntry"/> for the given <see cref="AccountTier"/>.
        /// </summary>
        public ImmutableList<RewardPropertyPremiumModifierEntry> GetRewardPropertiesForTier(AccountTier tier)
        {
            return rewardPropertiesByTier.TryGetValue(tier, out ImmutableList<RewardPropertyPremiumModifierEntry> entries) ? entries : ImmutableList<RewardPropertyPremiumModifierEntry>.Empty;
        }
    }
}
