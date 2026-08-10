using NexusForever.Database;

namespace NexusForever.Game.Map
{
    /// <summary>
    /// Tracks whether a zone-map discovery still needs to be committed.
    /// </summary>
    internal sealed class ZoneMapDiscoverySaveState
    {
        [Flags]
        private enum SaveMask
        {
            None   = 0x00,
            Create = 0x01
        }

        private readonly VersionedSaveMask<SaveMask> saveMask = new();

        /// <summary>
        /// Create discovery persistence state.
        /// </summary>
        /// <param name="pendingCreate">Whether the discovery has not yet been committed.</param>
        public ZoneMapDiscoverySaveState(bool pendingCreate)
        {
            if (pendingCreate)
                saveMask.Mark(SaveMask.Create);
        }

        /// <summary>
        /// Stage the pending discovery and acknowledge it only after a successful commit.
        /// </summary>
        /// <returns>Whether a pending discovery was staged.</returns>
        public bool Stage(ISaveCommitScope commitScope, Action stage)
        {
            ArgumentNullException.ThrowIfNull(commitScope);
            ArgumentNullException.ThrowIfNull(stage);

            VersionedSaveMaskSnapshot<SaveMask> snapshot = saveMask.Capture();
            if ((snapshot.Mask & SaveMask.Create) == 0)
                return false;

            stage.Invoke();
            commitScope.Register(() => saveMask.Acknowledge(snapshot));
            return true;
        }
    }
}
