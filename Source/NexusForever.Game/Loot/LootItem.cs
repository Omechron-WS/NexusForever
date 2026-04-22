using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.Loot;

namespace NexusForever.Game.Loot
{
    public class LootItem : ILootItem
    {
        public LootItemType Type { get; }
        public uint StaticId { get; }

        private readonly float probability;
        private readonly uint minCount;
        private readonly uint maxCount;

        public LootItem(LootItemModel model)
        {
            Type        = (LootItemType)model.Type;
            StaticId    = model.StaticId;
            probability = model.Probability;
            minCount    = model.MinCount;
            maxCount    = model.MaxCount;
        }

        /// <summary>
        /// Roll against probability and return whether the item drops, with the count.
        /// </summary>
        public bool GetDrop(out uint count)
        {
            count = 0;

            double chance = Random.Shared.NextDouble() * 100d;
            if (chance >= probability)
                return false;

            count = maxCount > minCount
                ? (uint)Random.Shared.Next((int)minCount, (int)maxCount + 1)
                : minCount;

            return true;
        }
    }
}
