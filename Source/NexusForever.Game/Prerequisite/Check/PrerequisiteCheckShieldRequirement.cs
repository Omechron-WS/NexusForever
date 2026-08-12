using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates a unit's current absolute shield.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.Shield216)]
    public class PrerequisiteCheckShieldRequirement : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckShieldRequirement> log;

        /// <summary>
        /// Creates a new current-shield prerequisite check.
        /// </summary>
        public PrerequisiteCheckShieldRequirement(
            ILogger<PrerequisiteCheckShieldRequirement> log)
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
                    $"Unsupported component shape {comparison}, {value}, {objectId} for {PrerequisiteType.Shield216}!");
                return false;
            }

            return Compare(player.Shield, comparison, value);
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

            meets = Compare(unit.Shield, comparison, value);
            return true;
        }

        private static bool Compare(
            uint currentShield,
            PrerequisiteComparison comparison,
            uint value)
        {
            return comparison switch
            {
                PrerequisiteComparison.Equal              => currentShield == value,
                PrerequisiteComparison.NotEqual           => currentShield != value,
                PrerequisiteComparison.GreaterThan        => currentShield > value,
                PrerequisiteComparison.GreaterThanOrEqual => currentShield >= value,
                PrerequisiteComparison.LessThan           => currentShield < value,
                PrerequisiteComparison.LessThanOrEqual    => currentShield <= value,
                _                                         => false
            };
        }
    }
}
