using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates a unit's current shield percentage using the build-16042 client calculation.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.Shield215)]
    public class PrerequisiteCheckShield : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckShield> log;

        /// <summary>
        /// Creates a new current-shield percentage prerequisite check.
        /// </summary>
        public PrerequisiteCheckShield(
            ILogger<PrerequisiteCheckShield> log)
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
                    $"Unsupported component shape {comparison}, {value}, {objectId} for {PrerequisiteType.Shield215}!");
                return false;
            }

            return Compare(player.Shield, player.MaxShieldCapacity, comparison, value);
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

            meets = Compare(unit.Shield, unit.MaxShieldCapacity, comparison, value);
            return true;
        }

        private static bool Compare(
            uint currentShield,
            uint maximumShield,
            PrerequisiteComparison comparison,
            uint value)
        {
            float percentage = (float)currentShield * 100f / (float)maximumShield;

            // The client uses an unordered SSE comparison without a parity check. For NaN,
            // Equal is therefore true while every other supported comparison is false.
            if (float.IsNaN(percentage))
                return comparison == PrerequisiteComparison.Equal;

            return comparison switch
            {
                PrerequisiteComparison.Equal              => percentage == value,
                PrerequisiteComparison.NotEqual           => percentage != value,
                PrerequisiteComparison.GreaterThan        => percentage > value,
                PrerequisiteComparison.GreaterThanOrEqual => percentage >= value,
                PrerequisiteComparison.LessThan           => percentage < value,
                PrerequisiteComparison.LessThanOrEqual    => percentage <= value,
                _                                         => false
            };
        }
    }
}
