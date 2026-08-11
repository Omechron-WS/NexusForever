using System.Collections.Immutable;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable.Model;
using NLog;

namespace NexusForever.Game.Quest
{
    /// <summary>
    /// Resolves concrete creature identifiers from nested target groups.
    /// </summary>
    internal sealed class TargetGroupResolver
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly IReadOnlyDictionary<uint, TargetGroupEntry> entries;
        private readonly HashSet<uint> reportedMissingGroups = [];
        private readonly HashSet<uint> reportedCyclicGroups = [];
        private readonly HashSet<TargetGroupType> reportedUnsupportedTypes = [];

        /// <summary>
        /// Create a resolver for the supplied target-group table entries.
        /// </summary>
        public TargetGroupResolver(IEnumerable<TargetGroupEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            this.entries = entries
                .Where(entry => entry != null)
                .GroupBy(entry => entry.Id)
                .ToDictionary(group => group.Key, group => group.First());
        }

        /// <summary>
        /// Resolve a target group to unique creature identifiers in deterministic table order.
        /// </summary>
        public ImmutableList<uint> ResolveCreatureIds(uint targetGroupId)
        {
            if (targetGroupId == 0u)
                return ImmutableList<uint>.Empty;

            var targetIds = ImmutableList.CreateBuilder<uint>();
            var addedTargets = new HashSet<uint>();
            var activePath = new HashSet<uint>();
            var expandedGroups = new HashSet<uint>();
            Expand(targetGroupId, targetIds, addedTargets, activePath, expandedGroups);
            return targetIds.ToImmutable();
        }

        private void Expand(
            uint targetGroupId,
            ImmutableList<uint>.Builder targetIds,
            HashSet<uint> addedTargets,
            HashSet<uint> activePath,
            HashSet<uint> expandedGroups)
        {
            if (expandedGroups.Contains(targetGroupId))
                return;

            if (!activePath.Add(targetGroupId))
            {
                if (reportedCyclicGroups.Add(targetGroupId))
                    log.Warn($"Target group {targetGroupId} contains a cyclic reference.");
                return;
            }

            if (!entries.TryGetValue(targetGroupId, out TargetGroupEntry entry))
            {
                if (reportedMissingGroups.Add(targetGroupId))
                    log.Warn($"Target group {targetGroupId} is missing from the game table.");
                activePath.Remove(targetGroupId);
                return;
            }

            switch ((TargetGroupType)entry.Type)
            {
                case TargetGroupType.CreatureIdGroup:
                    foreach (uint creatureId in entry.DataEntries ?? [])
                        if (creatureId != 0u && addedTargets.Add(creatureId))
                            targetIds.Add(creatureId);
                    break;
                case TargetGroupType.OtherTargetGroup:
                case TargetGroupType.OtherTargetGroupCreatures:
                    foreach (uint nestedGroupId in entry.DataEntries ?? [])
                        if (nestedGroupId != 0u)
                            Expand(nestedGroupId, targetIds, addedTargets, activePath, expandedGroups);
                    break;
                default:
                    TargetGroupType type = (TargetGroupType)entry.Type;
                    if (reportedUnsupportedTypes.Add(type))
                        log.Debug($"Target groups of type {entry.Type} are not concrete creature groups.");
                    break;
            }

            activePath.Remove(targetGroupId);
            expandedGroups.Add(targetGroupId);
        }
    }
}
