namespace NexusForever.Game.Static.Spell
{
    /// <summary>
    /// Flags controlling telegraph damage evaluation behaviour.
    /// Values are speculative, derived from comparing telegraph table entries.
    /// </summary>
    [Flags]
    public enum TelegraphDamageFlag
    {
        SpellMustBeMultiPhase = 0x0020,
        TargetMustBeUnit      = 0x0200,
        CasterMustBePlayer    = 0x0400,
        CasterMustBeNpc       = 0x1000
    }
}
