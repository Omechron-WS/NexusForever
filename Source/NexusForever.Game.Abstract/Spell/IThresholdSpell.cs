namespace NexusForever.Game.Abstract.Spell
{
    /// <summary>
    /// A root spell which consumes button transitions for a build-16042 threshold cast.
    /// </summary>
    public interface IThresholdSpell
    {
        /// <summary>
        /// Consume one button transition without creating another root spell.
        /// </summary>
        /// <returns><see langword="true"/> when the input belongs to this threshold root.</returns>
        bool TryHandleThresholdInput(bool buttonPressed);

        /// <summary>
        /// Return whether the supplied child is the exact child currently being admitted or already
        /// tracked by this root.
        /// </summary>
        bool IsValidThresholdChild(ISpell child);
    }
}
