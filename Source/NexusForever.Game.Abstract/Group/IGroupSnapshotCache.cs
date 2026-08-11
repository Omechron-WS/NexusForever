namespace NexusForever.Game.Abstract.Group
{
    /// <summary>
    /// Provides thread-safe access to immutable world-local group snapshots.
    /// </summary>
    /// <remarks>
    /// Snapshots represent the greatest validated group revision delivered to this world process. Revisions
    /// prevent delivered state from regressing, but do not prove freshness or complete delivery. Revision
    /// barriers are process-local and bounded, and group identifiers must not be reused while delayed broker
    /// messages can survive. Consumers must not treat this cache as the sole authority for a loot grant and
    /// must still revalidate live eligibility at allocation and delivery time.
    /// </remarks>
    public interface IGroupSnapshotCache
    {
        /// <summary>
        /// Try to retrieve the latest validated snapshot for a group.
        /// </summary>
        /// <param name="groupId">Authoritative group identifier.</param>
        /// <param name="snapshot">Latest immutable snapshot when one is available.</param>
        /// <returns>True when a snapshot is present; otherwise false.</returns>
        bool TryGet(ulong groupId, out GroupSnapshot snapshot);
    }
}
