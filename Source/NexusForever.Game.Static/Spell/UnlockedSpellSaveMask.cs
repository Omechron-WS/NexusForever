namespace NexusForever.Game.Static.Spell
{
    /// <summary>
    /// Flags controlling which unlocked spell fields are persisted to the database.
    /// </summary>
    [Flags]
    public enum UnlockedSpellSaveMask
    {
        None   = 0x0000,
        Create = 0x0001,
        Tier   = 0x0002
    }
}
