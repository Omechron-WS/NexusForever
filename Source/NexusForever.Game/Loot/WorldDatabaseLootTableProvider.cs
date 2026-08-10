using NexusForever.Database;
using NexusForever.Database.World;

namespace NexusForever.Game.Loot
{
    /// <summary>
    /// Loads loot table records from the configured world database.
    /// </summary>
    public sealed class WorldDatabaseLootTableProvider : ILootTableProvider
    {
        private readonly IDatabaseManager databaseManager;

        /// <summary>
        /// Create a world database loot table provider.
        /// </summary>
        public WorldDatabaseLootTableProvider(IDatabaseManager databaseManager)
        {
            this.databaseManager = databaseManager ?? throw new ArgumentNullException(nameof(databaseManager));
        }

        /// <inheritdoc />
        public LootTableData LoadLootTables()
        {
            WorldDatabase worldDatabase = databaseManager.GetDatabase<WorldDatabase>()
                ?? throw new InvalidOperationException("The world database must be initialised before loading loot tables.");

            return new LootTableData(
                worldDatabase.GetEntityLoot(),
                worldDatabase.GetItemLoot(),
                worldDatabase.GetLootGroups());
        }
    }
}
