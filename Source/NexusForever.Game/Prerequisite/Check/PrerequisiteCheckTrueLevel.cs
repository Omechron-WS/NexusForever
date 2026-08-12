using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates a unit's true, unscaled level.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.TrueLevel)]
    public class PrerequisiteCheckTrueLevel : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckTrueLevel> log;

        /// <summary>
        /// Creates a new true-level prerequisite check.
        /// </summary>
        public PrerequisiteCheckTrueLevel(
            ILogger<PrerequisiteCheckTrueLevel> log)
        {
            this.log = log;
        }

        #endregion

        /// <inheritdoc />
        public bool Meets(
            IPlayer player,
            PrerequisiteComparison comparison,
            uint value,
            uint objectId,
            IPrerequisiteParameters parameters)
        {
            if (player == null || !CanEvaluate(comparison, value, objectId))
            {
                log.LogWarning(
                    $"Unsupported component shape {comparison}, {value}, {objectId} for {PrerequisiteType.TrueLevel}!");
                return false;
            }

            return Compare(player.Level, comparison, value);
        }

        /// <inheritdoc />
        public bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId)
        {
            // Build 16042 does not read objectId and permits the full unsigned value domain.
            return comparison is (PrerequisiteComparison.Equal
                or PrerequisiteComparison.NotEqual
                or PrerequisiteComparison.GreaterThan
                or PrerequisiteComparison.GreaterThanOrEqual
                or PrerequisiteComparison.LessThan
                or PrerequisiteComparison.LessThanOrEqual);
        }

        /// <inheritdoc />
        public bool TryMeets(
            IUnitEntity unit,
            PrerequisiteComparison comparison,
            uint value,
            uint objectId,
            out bool meets)
        {
            meets = false;
            if (unit == null || !CanEvaluate(comparison, value, objectId))
                return false;

            meets = Compare(unit.Level, comparison, value);
            return true;
        }

        private static bool Compare(
            uint trueLevel,
            PrerequisiteComparison comparison,
            uint value)
        {
            return comparison switch
            {
                PrerequisiteComparison.Equal              => trueLevel == value,
                PrerequisiteComparison.NotEqual           => trueLevel != value,
                PrerequisiteComparison.GreaterThan        => trueLevel > value,
                PrerequisiteComparison.GreaterThanOrEqual => trueLevel >= value,
                PrerequisiteComparison.LessThan           => trueLevel < value,
                PrerequisiteComparison.LessThanOrEqual    => trueLevel <= value,
                _                                         => false
            };
        }
    }
}
