namespace NexusForever.Game.Abstract.Group
{
    /// <summary>
    /// Provides thread-safe access to immutable world-local group snapshots.
    /// </summary>
    /// <remarks>
    /// Snapshots represent the last validated group state delivered to this world process. Internal group
    /// messages do not carry revisions, so consumers must not treat this cache as the sole authority for a
    /// loot grant and must still revalidate live eligibility at allocation and delivery time.
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
