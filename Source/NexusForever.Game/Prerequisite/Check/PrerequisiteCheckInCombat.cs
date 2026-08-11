using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates whether a unit is currently in or out of combat.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.InCombat)]
    public class PrerequisiteCheckInCombat : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckInCombat> log;

        /// <summary>
        /// Creates a new combat-state prerequisite check.
        /// </summary>
        public PrerequisiteCheckInCombat(
            ILogger<PrerequisiteCheckInCombat> log)
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
            if (!CanEvaluate(comparison, value, objectId))
            {
                log.LogWarning(
                    $"Unsupported component shape {comparison}, {value}, {objectId} for {PrerequisiteType.InCombat}!");
                return false;
            }

            return comparison == PrerequisiteComparison.Equal
                ? player.InCombat
                : !player.InCombat;
        }

        /// <inheritdoc />
        public bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId)
        {
            return value == 0u
                && objectId == 0u
                && comparison is PrerequisiteComparison.Equal or PrerequisiteComparison.NotEqual;
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

            meets = comparison == PrerequisiteComparison.Equal
                ? unit.InCombat
                : !unit.InCombat;
            return true;
        }
    }
}
