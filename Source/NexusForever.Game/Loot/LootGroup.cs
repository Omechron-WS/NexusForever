using System.Collections.Generic;
using System.Linq;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Game.Static.Quest;

namespace NexusForever.Game.Loot
{
    public class LootGroup : ILootGroup
    {
        public ulong Id { get; }
        public float Probability { get; }

        private readonly uint minDrop;
        private readonly uint maxDrop;
        private readonly LootConditionType conditionType;
        private readonly uint condition;
        private readonly List<ILootGroup> childLootGroups = new();
        private readonly List<ILootItem> lootItems = new();

        public LootGroup(LootGroupModel model)
        {
            Id            = model.Id;
            Probability   = model.Probability;
            minDrop       = model.MinDrop;
            maxDrop       = model.MaxDrop >= model.MinDrop ? model.MaxDrop : model.MinDrop;
            conditionType = (LootConditionType)model.ConditionType;
            condition     = model.Condition;

            foreach (LootGroupModel child in model.ChildGroup)
                childLootGroups.Add(new LootGroup(child));

            foreach (LootItemModel item in model.Item)
                lootItems.Add(new LootItem(item));
        }

        /// <summary>
        /// Generate loot drops for the given player, evaluating probability and conditions.
        /// </summary>
        public Dictionary<ILootItem, uint> GenerateLootDrops(IPlayer player)
        {
            if (player == null)
                return new Dictionary<ILootItem, uint>();

            if (!WillDrop(player))
                return new Dictionary<ILootItem, uint>();

            var drops = new Dictionary<ILootItem, uint>();

            foreach (ILootGroup child in childLootGroups)
            {
                Dictionary<ILootItem, uint> childDrops = child.GenerateLootDrops(player);
                foreach ((ILootItem item, uint count) in childDrops)
                    drops[item] = count;
            }

            foreach (ILootItem lootItem in lootItems)
                if (lootItem.GetDrop(out uint count))
                    drops[lootItem] = count;

            if (maxDrop > 0 && drops.Count > maxDrop)
            {
                var keys = drops.Keys.ToList();
                while (drops.Count > maxDrop)
                {
                    int index = Random.Shared.Next(keys.Count);
                    drops.Remove(keys[index]);
                    keys.RemoveAt(index);
                }
            }

            if (minDrop > 0 && drops.Count < minDrop)
            {
                int attempts = 0;
                while (drops.Count < minDrop && attempts < 100)
                {
                    foreach (ILootItem lootItem in lootItems)
                    {
                        if (drops.ContainsKey(lootItem))
                            continue;

                        if (lootItem.GetDrop(out uint count))
                            drops[lootItem] = count;

                        if (drops.Count >= minDrop)
                            break;
                    }

                    attempts++;
                }
            }

            return drops;
        }

        private bool WillDrop(IPlayer player)
        {
            if (!MeetsCondition(player))
                return false;

            if (!float.IsFinite(Probability) || Probability <= 0f || Probability > 100f)
                return false;

            double chance = Random.Shared.NextDouble() * 100d;
            return chance < Probability;
        }

        private bool MeetsCondition(IPlayer player)
        {
            if (player == null)
                return false;

            switch (conditionType)
            {
                case LootConditionType.None:
                    return true;
                case LootConditionType.IsClass:
                    return IsDefinedClass(condition) && (uint)player.Class == condition;
                case LootConditionType.IsRace:
                    return IsDefinedRace(condition) && (uint)player.Race == condition;
                case LootConditionType.IsLevel:
                    return HasValidLevelContext(player) && condition > 0u && player.Level == condition;
                case LootConditionType.IsLessThanLevel:
                    return HasValidLevelContext(player) && condition > 0u && player.Level < condition;
                case LootConditionType.IsMoreThanLevel:
                    return HasValidLevelContext(player) && condition > 0u && player.Level > condition;
                case LootConditionType.QuestIsComplete:
                    return TryGetQuestState(player, out QuestState? completedState)
                        && completedState == QuestState.Completed;
                case LootConditionType.QuestNotComplete:
                    return TryGetQuestState(player, out QuestState? incompleteState)
                        && incompleteState != QuestState.Completed;
                case LootConditionType.QuestObjectiveActive:
                    return HasActiveQuestObjective(player);
                default:
                    return false;
            }
        }

        private static bool HasValidLevelContext(IPlayer player)
        {
            return player.Level > 0u;
        }

        private static bool IsDefinedClass(uint value)
        {
            return value is > 0u and <= byte.MaxValue
                && Enum.IsDefined((Class)value);
        }

        private static bool IsDefinedRace(uint value)
        {
            return value is > 0u and <= byte.MaxValue
                && Enum.IsDefined((Race)value);
        }

        private bool TryGetQuestState(IPlayer player, out QuestState? state)
        {
            state = null;
            if (condition == 0u || condition > ushort.MaxValue || player.QuestManager == null)
                return false;

            state = player.QuestManager.GetQuestState((ushort)condition);
            return true;
        }

        private bool HasActiveQuestObjective(IPlayer player)
        {
            if (condition == 0u || player.QuestManager == null)
                return false;

            IEnumerable<IQuest> activeQuests = player.QuestManager.GetActiveQuests();
            if (activeQuests == null)
                return false;

            return activeQuests
                .Where(quest => quest != null)
                .SelectMany(quest => quest)
                .Any(objective => objective?.ObjectiveInfo?.Id == condition && !objective.IsComplete());
        }
    }
}
