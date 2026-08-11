using System.Collections.Immutable;
using NexusForever.Game.Static.Group;

namespace NexusForever.Game.Abstract.Group
{
    /// <summary>
    /// Immutable, stable identity and state for a member of a group snapshot.
    /// </summary>
    public sealed record GroupMemberSnapshot
    {
        /// <summary>
        /// Character identifier within the member's realm.
        /// </summary>
        public required ulong CharacterId { get; init; }

        /// <summary>
        /// Realm identifier for the member.
        /// </summary>
        public required ushort RealmId { get; init; }

        /// <summary>
        /// Authoritative group slot assigned to the member.
        /// </summary>
        public required uint GroupIndex { get; init; }

        /// <summary>
        /// Validated group-member state flags.
        /// </summary>
        public required GroupMemberInfoFlags Flags { get; init; }
    }

    /// <summary>
    /// Immutable world-local view of the authoritative state needed for group loot.
    /// </summary>
    public sealed record GroupSnapshot
    {
        /// <summary>
        /// Authoritative group identifier.
        /// </summary>
        public required ulong Id { get; init; }

        /// <summary>
        /// Validated group state flags.
        /// </summary>
        public required GroupFlags Flags { get; init; }

        /// <summary>
        /// Loot rule for items below the configured quality threshold.
        /// </summary>
        public required LootRule NormalRule { get; init; }

        /// <summary>
        /// Loot rule for items at or above the configured quality threshold.
        /// </summary>
        public required LootRule ThresholdRule { get; init; }

        /// <summary>
        /// Quality threshold separating the two loot rules.
        /// </summary>
        public required LootThreshold ThresholdQuality { get; init; }

        /// <summary>
        /// Harvest loot rule for the group.
        /// </summary>
        public required HarvestLootRule HarvestRule { get; init; }

        /// <summary>
        /// Character identifier of the current group leader.
        /// </summary>
        public required ulong LeaderCharacterId { get; init; }

        /// <summary>
        /// Realm identifier of the current group leader.
        /// </summary>
        public required ushort LeaderRealmId { get; init; }

        /// <summary>
        /// Immutable authoritative member collection.
        /// </summary>
        public required ImmutableArray<GroupMemberSnapshot> Members { get; init; }
    }
}
