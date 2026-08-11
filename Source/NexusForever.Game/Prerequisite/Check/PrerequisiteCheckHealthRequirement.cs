using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates a unit's current absolute health.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.HealthRequirement)]
    public class PrerequisiteCheckHealthRequirement : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckHealthRequirement> log;

        /// <summary>
        /// Creates a new current-health prerequisite check.
        /// </summary>
        public PrerequisiteCheckHealthRequirement(
            ILogger<PrerequisiteCheckHealthRequirement> log)
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
                    $"Unsupported component shape {comparison}, {value}, {objectId} for {PrerequisiteType.HealthRequirement}!");
                return false;
            }

            return Compare(player.Health, comparison, value);
        }

        /// <inheritdoc />
        public bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId)
        {
            return objectId == 0u
                && comparison is (PrerequisiteComparison.Equal
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

            meets = Compare(unit.Health, comparison, value);
            return true;
        }

        private static bool Compare(
            uint currentHealth,
            PrerequisiteComparison comparison,
            uint value)
        {
            return comparison switch
            {
                PrerequisiteComparison.Equal              => currentHealth == value,
                PrerequisiteComparison.NotEqual           => currentHealth != value,
                PrerequisiteComparison.GreaterThan        => currentHealth > value,
                PrerequisiteComparison.GreaterThanOrEqual => currentHealth >= value,
                PrerequisiteComparison.LessThan           => currentHealth < value,
                PrerequisiteComparison.LessThanOrEqual    => currentHealth <= value,
                _                                         => false
            };
        }
    }
}
