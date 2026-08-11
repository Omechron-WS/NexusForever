using System.Collections.Concurrent;
using System.Collections.Immutable;
using NexusForever.Game.Abstract.Group;
using NexusForever.Game.Static.Group;
using InternalGroup = NexusForever.Network.Internal.Message.Group.Shared.Group;
using InternalIdentity = NexusForever.Network.Internal.Message.Shared.Identity;

namespace NexusForever.Game.Group
{
    /// <summary>
    /// Stores validated immutable copies of authoritative group-server messages.
    /// </summary>
    /// <remarks>
    /// Up to 65,536 rejected or disbanded group revision barriers are retained in first-observation order.
    /// This bounds memory while covering a conservative delayed-delivery window. Once a barrier is evicted, an
    /// arbitrarily late message for that identifier can create a snapshot again. Revisions prevent delivered state
    /// from regressing, but do not prove freshness or complete delivery. The cache is therefore not sole loot authority.
    /// </remarks>
    public sealed class GroupSnapshotCache : IGroupSnapshotCache
    {
        internal const int DefaultRevisionBarrierCapacity = 65_536;

        private const GroupFlags SupportedGroupFlags = GroupFlags.OpenWorld
            | GroupFlags.Raid
            | GroupFlags.JoinRequestOpen
            | GroupFlags.JoinRequestClosed
            | GroupFlags.ReferralsOpen
            | GroupFlags.ReferralsClosed;

        private const GroupMemberInfoFlags SupportedMemberFlags = GroupMemberInfoFlags.CanInvite
            | GroupMemberInfoFlags.CanKick
            | GroupMemberInfoFlags.Disconnected
            | GroupMemberInfoFlags.Pending
            | GroupMemberInfoFlags.Tank
            | GroupMemberInfoFlags.Healer
            | GroupMemberInfoFlags.DPS
            | GroupMemberInfoFlags.MainTank
            | GroupMemberInfoFlags.MainAssist
            | GroupMemberInfoFlags.RaidAssistant
            | GroupMemberInfoFlags.Ready
            | GroupMemberInfoFlags.RoleLocked
            | GroupMemberInfoFlags.CanMark
            | GroupMemberInfoFlags.HasSetReady;

        private readonly ConcurrentDictionary<ulong, GroupSnapshot> snapshots = [];
        private readonly Dictionary<ulong, RevisionBarrier> revisionBarriers = [];
        private readonly LinkedList<ulong> revisionBarrierOrder = [];
        private readonly object mutationSyncRoot = new();
        private readonly int revisionBarrierCapacity;

        private sealed class RevisionBarrier
        {
            public required ulong Revision { get; set; }
            public required bool Terminal { get; set; }
            public required LinkedListNode<ulong> OrderNode { get; init; }
        }

        /// <summary>
        /// Create a group snapshot cache with the production revision-barrier capacity.
        /// </summary>
        public GroupSnapshotCache()
            : this(DefaultRevisionBarrierCapacity)
        {
        }

        internal GroupSnapshotCache(int revisionBarrierCapacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revisionBarrierCapacity);
            this.revisionBarrierCapacity = revisionBarrierCapacity;
        }

        /// <inheritdoc />
        public bool TryGet(ulong groupId, out GroupSnapshot snapshot)
        {
            if (groupId == 0ul)
            {
                snapshot = null;
                return false;
            }

            return snapshots.TryGetValue(groupId, out snapshot);
        }

        /// <summary>
        /// Validate and atomically apply an authoritative group payload by revision.
        /// A malformed payload at or above the current revision evicts stale authorisation and records a barrier.
        /// </summary>
        /// <param name="group">Mutable internal group payload to copy.</param>
        /// <returns>True when a validated snapshot was stored; otherwise false.</returns>
        public bool TryUpsert(InternalGroup group)
        {
            if (!TryCreateSnapshot(group, null, out GroupSnapshot snapshot))
            {
                RejectMalformed(group);
                return false;
            }

            return TryStoreSnapshot(snapshot);
        }

        /// <summary>
        /// Apply a member-removal payload whose source group still contains the removed member.
        /// An inconsistent removal evicts the group rather than retaining stale authorisation.
        /// </summary>
        /// <param name="group">Mutable internal group payload to copy.</param>
        /// <param name="removedIdentity">Stable identity to subtract from the copied members.</param>
        /// <returns>True when a validated post-removal snapshot was stored; otherwise false.</returns>
        public bool TryApplyMemberRemoval(InternalGroup group, InternalIdentity removedIdentity)
        {
            ulong groupId = group?.Id ?? 0ul;
            if (!IsValidIdentity(removedIdentity)
                || !TryCreateSnapshot(group, removedIdentity, out GroupSnapshot snapshot))
            {
                if (group != null && groupId != 0ul && group.Revision != 0ul)
                    Reject(groupId, group.Revision);

                return false;
            }

            return TryStoreSnapshot(snapshot);
        }

        /// <summary>
        /// Reject an invalid authoritative payload without allowing stale invalid data to evict newer state.
        /// </summary>
        /// <param name="groupId">Authoritative group identifier.</param>
        /// <param name="revision">Revision carried by the invalid payload.</param>
        /// <returns>True when the invalid payload established or advanced a rejection barrier.</returns>
        public bool Reject(ulong groupId, ulong revision)
        {
            if (groupId == 0ul || revision == 0ul)
                return false;

            lock (mutationSyncRoot)
            {
                if (revisionBarriers.TryGetValue(groupId, out RevisionBarrier barrier))
                {
                    if (barrier.Terminal || revision <= barrier.Revision)
                        return false;

                    barrier.Revision = revision;
                    return true;
                }

                if (snapshots.TryGetValue(groupId, out GroupSnapshot snapshot)
                    && revision < snapshot.Revision)
                    return false;

                snapshots.TryRemove(groupId, out _);
                SetRevisionBarrier(groupId, revision, terminal: false);
                return true;
            }
        }

        /// <summary>
        /// Atomically evict and add a bounded terminal revision barrier for a disbanded group.
        /// </summary>
        /// <remarks>
        /// Duplicate delivery does not consume additional capacity. Group identifiers must not be reused. When
        /// capacity is exceeded, the oldest barrier is evicted and can no longer reject arbitrarily delayed messages.
        /// </remarks>
        /// <param name="groupId">Authoritative group identifier.</param>
        /// <param name="revision">Terminal group revision.</param>
        /// <returns>True when the terminal revision was applied or was an identical duplicate; otherwise false.</returns>
        public bool MarkDisbanded(ulong groupId, ulong revision)
        {
            if (groupId == 0ul || revision == 0ul)
                return false;

            lock (mutationSyncRoot)
            {
                if (revisionBarriers.TryGetValue(groupId, out RevisionBarrier barrier))
                {
                    if (revision < barrier.Revision)
                        return false;

                    if (barrier.Terminal && revision == barrier.Revision)
                        return true;

                    barrier.Revision = revision;
                    barrier.Terminal = true;
                    snapshots.TryRemove(groupId, out _);
                    return true;
                }

                if (snapshots.TryGetValue(groupId, out GroupSnapshot snapshot)
                    && revision < snapshot.Revision)
                    return false;

                snapshots.TryRemove(groupId, out _);
                SetRevisionBarrier(groupId, revision, terminal: true);
            }

            return true;
        }

        private bool TryStoreSnapshot(GroupSnapshot snapshot)
        {
            lock (mutationSyncRoot)
            {
                if (revisionBarriers.TryGetValue(snapshot.Id, out RevisionBarrier barrier))
                {
                    if (barrier.Terminal || snapshot.Revision <= barrier.Revision)
                        return false;

                    RemoveRevisionBarrier(snapshot.Id);
                }

                if (snapshots.TryGetValue(snapshot.Id, out GroupSnapshot current))
                {
                    if (snapshot.Revision < current.Revision)
                        return false;

                    if (snapshot.Revision == current.Revision)
                    {
                        if (HasEquivalentAuthority(current, snapshot))
                            return true;

                        snapshots.TryRemove(snapshot.Id, out _);
                        SetRevisionBarrier(snapshot.Id, snapshot.Revision, terminal: false);
                        return false;
                    }
                }

                snapshots[snapshot.Id] = snapshot;
                return true;
            }
        }

        private void RejectMalformed(InternalGroup group)
        {
            if (group != null && group.Id != 0ul && group.Revision != 0ul)
                Reject(group.Id, group.Revision);
        }

        private void SetRevisionBarrier(ulong groupId, ulong revision, bool terminal)
        {
            if (revisionBarriers.TryGetValue(groupId, out RevisionBarrier existing))
            {
                existing.Revision = revision;
                existing.Terminal = terminal;
                return;
            }

            LinkedListNode<ulong> orderNode = revisionBarrierOrder.AddLast(groupId);
            revisionBarriers.Add(groupId, new RevisionBarrier
            {
                Revision  = revision,
                Terminal  = terminal,
                OrderNode = orderNode,
            });

            if (revisionBarriers.Count <= revisionBarrierCapacity)
                return;

            ulong expiredGroupId = revisionBarrierOrder.First.Value;
            RemoveRevisionBarrier(expiredGroupId);
        }

        private void RemoveRevisionBarrier(ulong groupId)
        {
            if (!revisionBarriers.Remove(groupId, out RevisionBarrier barrier))
                return;

            revisionBarrierOrder.Remove(barrier.OrderNode);
        }

        private static bool HasEquivalentAuthority(GroupSnapshot first, GroupSnapshot second)
        {
            if (first.Id != second.Id
                || first.Revision != second.Revision
                || first.Flags != second.Flags
                || first.NormalRule != second.NormalRule
                || first.ThresholdRule != second.ThresholdRule
                || first.ThresholdQuality != second.ThresholdQuality
                || first.HarvestRule != second.HarvestRule
                || first.LeaderCharacterId != second.LeaderCharacterId
                || first.LeaderRealmId != second.LeaderRealmId
                || first.Members.Length != second.Members.Length)
                return false;

            for (int i = 0; i < first.Members.Length; i++)
            {
                GroupMemberSnapshot firstMember = first.Members[i];
                GroupMemberSnapshot secondMember = second.Members[i];
                if (firstMember.CharacterId != secondMember.CharacterId
                    || firstMember.RealmId != secondMember.RealmId
                    || firstMember.GroupIndex != secondMember.GroupIndex
                    || firstMember.Flags != secondMember.Flags)
                    return false;
            }

            return true;
        }

        private static bool TryCreateSnapshot(
            InternalGroup group,
            InternalIdentity removedIdentity,
            out GroupSnapshot snapshot)
        {
            snapshot = null;

            if (group == null
                || group.Id == 0ul
                || group.Revision == 0ul
                || group.Leader == null
                || !IsValidIdentity(group.Leader)
                || group.Members == null
                || group.Members.Count == 0
                || (group.Flags & ~SupportedGroupFlags) != 0
                || !Enum.IsDefined(group.NormalRule)
                || !Enum.IsDefined(group.ThresholdRule)
                || !Enum.IsDefined(group.ThresholdQuality)
                || !Enum.IsDefined(group.HarvestRule))
                return false;

            bool removeMember = removedIdentity != null;
            if (removeMember && !IsValidIdentity(removedIdentity))
                return false;

            var identities = new HashSet<(ulong CharacterId, ushort RealmId)>();
            var indexes = new HashSet<uint>();
            var members = new List<GroupMemberSnapshot>(group.Members.Count);
            bool removedMemberFound = false;

            foreach (Network.Internal.Message.Group.Shared.GroupMember member in group.Members)
            {
                if (member?.Identity == null
                    || !IsValidIdentity(member.Identity)
                    || member.GroupIndex == 0u
                    || (member.Flags & ~SupportedMemberFlags) != 0)
                    return false;

                var identity = (member.Identity.Id, member.Identity.RealmId);
                if (!identities.Add(identity) || !indexes.Add(member.GroupIndex))
                    return false;

                if (removeMember
                    && member.Identity.Id == removedIdentity.Id
                    && member.Identity.RealmId == removedIdentity.RealmId)
                {
                    removedMemberFound = true;
                    continue;
                }

                members.Add(new GroupMemberSnapshot
                {
                    CharacterId = member.Identity.Id,
                    RealmId     = member.Identity.RealmId,
                    GroupIndex  = member.GroupIndex,
                    Flags       = member.Flags,
                });
            }

            if (removeMember && !removedMemberFound)
                return false;

            if (members.Count == 0
                || !members.Any(member => member.CharacterId == group.Leader.Id
                    && member.RealmId == group.Leader.RealmId))
                return false;

            snapshot = new GroupSnapshot
            {
                Id                = group.Id,
                Revision          = group.Revision,
                Flags             = group.Flags,
                NormalRule        = group.NormalRule,
                ThresholdRule     = group.ThresholdRule,
                ThresholdQuality  = group.ThresholdQuality,
                HarvestRule       = group.HarvestRule,
                LeaderCharacterId = group.Leader.Id,
                LeaderRealmId     = group.Leader.RealmId,
                Members           = members
                    .OrderBy(member => member.GroupIndex)
                    .ToImmutableArray(),
            };

            return true;
        }

        private static bool IsValidIdentity(InternalIdentity identity)
        {
            return identity != null && identity.Id != 0ul && identity.RealmId != 0;
        }

    }
}
