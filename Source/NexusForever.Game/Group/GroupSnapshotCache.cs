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
    /// Up to 65,536 distinct disbanded group identifiers are retained as FIFO tombstones by first observation.
    /// This bounds memory while covering a conservative delayed-delivery window. Once a tombstone is evicted,
    /// an arbitrarily late message for that identifier can create a snapshot again because group messages contain
    /// no revision. The cache therefore represents the last validated delivered state and is not sole loot authority.
    /// </remarks>
    public sealed class GroupSnapshotCache : IGroupSnapshotCache
    {
        internal const int DefaultDisbandTombstoneCapacity = 65_536;

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
        private readonly HashSet<ulong> disbandTombstones = [];
        private readonly Queue<ulong> disbandTombstoneOrder = [];
        private readonly object mutationSyncRoot = new();
        private readonly int disbandTombstoneCapacity;

        /// <summary>
        /// Create a group snapshot cache with the production tombstone capacity.
        /// </summary>
        public GroupSnapshotCache()
            : this(DefaultDisbandTombstoneCapacity)
        {
        }

        internal GroupSnapshotCache(int disbandTombstoneCapacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(disbandTombstoneCapacity);
            this.disbandTombstoneCapacity = disbandTombstoneCapacity;
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
        /// Validate and atomically replace the snapshot for an authoritative group payload.
        /// Invalid payloads leave the previous valid snapshot unchanged.
        /// </summary>
        /// <param name="group">Mutable internal group payload to copy.</param>
        /// <returns>True when a validated snapshot was stored; otherwise false.</returns>
        public bool TryUpsert(InternalGroup group)
        {
            if (!TryCreateSnapshot(group, null, out GroupSnapshot snapshot))
                return false;

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
                Evict(groupId);
                return false;
            }

            return TryStoreSnapshot(snapshot);
        }

        /// <summary>
        /// Evict live state for a group without clearing a disband tombstone.
        /// </summary>
        /// <param name="groupId">Authoritative group identifier.</param>
        public void Evict(ulong groupId)
        {
            if (groupId == 0ul)
                return;

            lock (mutationSyncRoot)
                snapshots.TryRemove(groupId, out _);
        }

        /// <summary>
        /// Atomically evict and add a bounded tombstone for a disbanded group.
        /// </summary>
        /// <remarks>
        /// Duplicate delivery does not consume additional capacity. When capacity is exceeded, the oldest
        /// tombstone is evicted deterministically and can no longer reject arbitrarily delayed stale messages.
        /// </remarks>
        /// <param name="groupId">Authoritative group identifier.</param>
        /// <returns>True when the identifier was valid; otherwise false.</returns>
        public bool MarkDisbanded(ulong groupId)
        {
            if (groupId == 0ul)
                return false;

            lock (mutationSyncRoot)
            {
                snapshots.TryRemove(groupId, out _);
                if (!disbandTombstones.Add(groupId))
                    return true;

                disbandTombstoneOrder.Enqueue(groupId);
                if (disbandTombstoneOrder.Count > disbandTombstoneCapacity)
                {
                    ulong expiredGroupId = disbandTombstoneOrder.Dequeue();
                    disbandTombstones.Remove(expiredGroupId);
                }
            }

            return true;
        }

        private bool TryStoreSnapshot(GroupSnapshot snapshot)
        {
            lock (mutationSyncRoot)
            {
                if (disbandTombstones.Contains(snapshot.Id))
                    return false;

                snapshots[snapshot.Id] = snapshot;
                return true;
            }
        }

        private static bool TryCreateSnapshot(
            InternalGroup group,
            InternalIdentity removedIdentity,
            out GroupSnapshot snapshot)
        {
            snapshot = null;

            if (group == null
                || group.Id == 0ul
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
            var members = ImmutableArray.CreateBuilder<GroupMemberSnapshot>(group.Members.Count);
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
                Flags             = group.Flags,
                NormalRule        = group.NormalRule,
                ThresholdRule     = group.ThresholdRule,
                ThresholdQuality  = group.ThresholdQuality,
                HarvestRule       = group.HarvestRule,
                LeaderCharacterId = group.Leader.Id,
                LeaderRealmId     = group.Leader.RealmId,
                Members           = members.ToImmutable(),
            };

            return true;
        }

        private static bool IsValidIdentity(InternalIdentity identity)
        {
            return identity != null && identity.Id != 0ul && identity.RealmId != 0;
        }

    }
}
