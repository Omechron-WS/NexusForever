using System.Collections.Generic;
using System.Numerics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Loot
{
    public interface ILootInstance : IUpdate, IEnumerable<ILootInstanceItem>
    {
        /// <summary>
        /// Entity guid that dropped this loot.
        /// </summary>
        uint Guid { get; }

        LootEntityType LootEntityType { get; }
        LooterType LooterType { get; }
        Vector3 Position { get; }
        bool HasExpired { get; }
        bool Explosion { get; set; }

        /// <summary>
        /// Add a loot item to this instance.
        /// </summary>
        void AddLootItem(uint staticId, LootItemType type, uint count);

        /// <summary>
        /// Send the loot notification packet to the given player.
        /// </summary>
        void SendLootNotify(IPlayer player);

        /// <summary>
        /// Returns whether a loot instance item with the given id exists.
        /// </summary>
        bool HasLootInstanceId(uint id);

        /// <summary>
        /// Returns whether the character is an authorised looter.
        /// </summary>
        bool HasLooter(ulong characterId);
    }
}
