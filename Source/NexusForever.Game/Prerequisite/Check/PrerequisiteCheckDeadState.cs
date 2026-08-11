using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates whether a unit is currently alive or dead.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.DeadState)]
    public class PrerequisiteCheckDeadState : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckDeadState> log;

        /// <summary>
        /// Creates a new death-state prerequisite check.
        /// </summary>
        public PrerequisiteCheckDeadState(
            ILogger<PrerequisiteCheckDeadState> log)
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
                    $"Unsupported component shape {comparison}, {value}, {objectId} for {PrerequisiteType.DeadState}!");
                return false;
            }

            return Compare(!player.IsAlive, comparison, value);
        }

        /// <inheritdoc />
        public bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId)
        {
            return value is 0u or 1u
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

            meets = Compare(!unit.IsAlive, comparison, value);
            return true;
        }

        private static bool Compare(
            bool isDead,
            PrerequisiteComparison comparison,
            uint value)
        {
            bool requiredDeadState = value == 1u;
            return comparison == PrerequisiteComparison.Equal
                ? isDead == requiredDeadState
                : isDead != requiredDeadState;
        }
    }
}
