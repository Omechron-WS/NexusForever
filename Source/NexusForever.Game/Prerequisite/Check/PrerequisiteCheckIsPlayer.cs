using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates whether a unit is a player.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.IsPlayer)]
    public class PrerequisiteCheckIsPlayer : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckIsPlayer> log;

        /// <summary>
        /// Creates a new player-identity prerequisite check.
        /// </summary>
        public PrerequisiteCheckIsPlayer(
            ILogger<PrerequisiteCheckIsPlayer> log)
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
                    $"Unsupported component shape {comparison}, {value}, {objectId} for {PrerequisiteType.IsPlayer}!");
                return false;
            }

            return comparison == PrerequisiteComparison.Equal;
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

            bool isPlayer = unit is IPlayer;
            meets = comparison == PrerequisiteComparison.Equal
                ? isPlayer
                : !isPlayer;
            return true;
        }
    }
}
