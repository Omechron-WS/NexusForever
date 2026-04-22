using System.Collections.Generic;
using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Game.Abstract.Loot
{
    public interface ILootGroup
    {
        ulong Id { get; }
        float Probability { get; }

        /// <summary>
        /// Generate loot drops for the given player, evaluating probability and conditions.
        /// </summary>
        Dictionary<ILootItem, uint> GenerateLootDrops(IPlayer player);
    }
}
