using System.Collections.Generic;
using System.Linq;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
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
            double chance = Random.Shared.NextDouble() * 100d;
            if (chance >= Probability)
                return false;

            return MeetsCondition(player);
        }

        private bool MeetsCondition(IPlayer player)
        {
            switch (conditionType)
            {
                case LootConditionType.None:
                    return true;
                case LootConditionType.QuestObjectiveActive:
                    // TODO: properly check if player has an active quest objective matching this condition value.
                    // Requires IQuestManager to expose an objective query method.
                    // For now, return true (permissive) to avoid blocking loot drops.
                    return true;
                default:
                    // TODO: implement IsClass, IsRace, IsLevel, QuestIsComplete, etc.
                    return true;
            }
        }
    }
}
