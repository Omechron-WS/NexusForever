namespace NexusForever.Game.Loot
{
    /// <summary>
    /// Supplies the world database records used to build the global loot cache.
    /// </summary>
    public interface ILootTableProvider
    {
        /// <summary>
        /// Load the complete set of loot mappings, groups, and items.
        /// </summary>
        LootTableData LoadLootTables();
    }
}
