namespace NexusForever.Game.Abstract.Spell
{
    /// <summary>
    /// A spell whose terminal state is controlled by a build-16042 client-side interaction result.
    /// </summary>
    public interface IClientSideInteractionSpell : ISpell
    {
        /// <summary>
        /// Gets whether the spell is still awaiting its first terminal interaction result.
        /// </summary>
        bool IsInteractionPending { get; }

        /// <summary>
        /// Gets whether the interaction table entry requires a client-reported terminal result.
        /// </summary>
        bool RequiresClientResult { get; }

        /// <summary>
        /// Complete the interaction successfully.
        /// </summary>
        /// <returns><see langword="true"/> when this result claimed the interaction; otherwise, <see langword="false"/>.</returns>
        bool SucceedClientInteraction();

        /// <summary>
        /// Complete the interaction unsuccessfully.
        /// </summary>
        /// <returns><see langword="true"/> when this result claimed the interaction; otherwise, <see langword="false"/>.</returns>
        bool FailClientInteraction();

        /// <summary>
        /// Cancel the interaction without invoking its failure callback.
        /// </summary>
        /// <returns><see langword="true"/> when this result claimed the interaction; otherwise, <see langword="false"/>.</returns>
        bool CancelClientInteraction();
    }
}
