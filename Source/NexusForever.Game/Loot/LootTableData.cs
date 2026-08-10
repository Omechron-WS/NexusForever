using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NexusForever.Database.World.Model;

namespace NexusForever.Game.Loot
{
    /// <summary>
    /// Immutable collection wrapper for world database loot records.
    /// </summary>
    public sealed class LootTableData
    {
        /// <summary>
        /// Creature-to-loot-group mappings.
        /// </summary>
        public ImmutableList<EntityLootModel> EntityLoot { get; }

        /// <summary>
        /// Item-to-loot-group mappings.
        /// </summary>
        public ImmutableList<ItemLootModel> ItemLoot { get; }

        /// <summary>
        /// Flat collection of all loot groups with their directly-owned items.
        /// </summary>
        public ImmutableList<LootGroupModel> LootGroups { get; }

        /// <summary>
        /// Create a loot table data set from database records.
        /// </summary>
        public LootTableData(
            IEnumerable<EntityLootModel> entityLoot,
            IEnumerable<ItemLootModel> itemLoot,
            IEnumerable<LootGroupModel> lootGroups)
        {
            EntityLoot = entityLoot?.ToImmutableList() ?? throw new ArgumentNullException(nameof(entityLoot));
            ItemLoot   = itemLoot?.ToImmutableList() ?? throw new ArgumentNullException(nameof(itemLoot));
            LootGroups = lootGroups?.ToImmutableList() ?? throw new ArgumentNullException(nameof(lootGroups));
        }
    }
}
