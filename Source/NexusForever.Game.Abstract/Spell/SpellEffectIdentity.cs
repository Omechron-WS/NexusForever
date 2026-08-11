namespace NexusForever.Game.Abstract.Spell
{
    /// <summary>
    /// Identifies one logical effect row owned by a spell cast.
    /// </summary>
    /// <param name="CastingId">Server casting identifier.</param>
    /// <param name="Spell4Id">Source Spell4 identifier.</param>
    /// <param name="Spell4EffectId">Static Spell4Effects row identifier.</param>
    public readonly record struct SpellEffectIdentity(
        uint CastingId,
        uint Spell4Id,
        uint Spell4EffectId);
}
