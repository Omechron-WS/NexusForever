namespace NexusForever.Game.Static.Spell
{
    /// <summary>
    /// Flags indicating what target attributes a spell requires for targeting.
    /// </summary>
    [Flags]
    public enum SpellTargetMechanicFlags
    {
        None               = 0x0000,
        IsPlayer           = 0x0001,
        IsFriendly         = 0x0008,
        IsEnemy            = 0x0010,
        AlsoIncludeEnemies = 0x0020
    }
}
