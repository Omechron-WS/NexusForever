using NexusForever.Game.Static.Loot;

namespace NexusForever.Game.Abstract.Loot
{
    public interface ILootItem
    {
        LootItemType Type { get; }
        uint StaticId { get; }

        /// <summary>
        /// Roll against probability and return whether the item drops, with the count.
        /// </summary>
        bool GetDrop(out uint count);
    }
}
