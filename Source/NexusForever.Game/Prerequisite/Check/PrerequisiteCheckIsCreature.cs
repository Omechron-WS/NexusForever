using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates a unit's exact creature identifier.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.IsCreature)]
    public class PrerequisiteCheckIsCreature : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckIsCreature> log;

        /// <summary>
        /// Creates a new creature-identity prerequisite check.
        /// </summary>
        public PrerequisiteCheckIsCreature(
            ILogger<PrerequisiteCheckIsCreature> log)
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
                    $"Unsupported component shape {comparison}, {value}, {objectId} for {PrerequisiteType.IsCreature}!");
                return false;
            }

            return Evaluate(player.CreatureId, comparison, value);
        }

        /// <inheritdoc />
        public bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId)
        {
            // Build 16042 does not read objectId and permits the full unsigned creature-id domain,
            // including zero when the subject has no Creature2 entry.
            return comparison is PrerequisiteComparison.Equal or PrerequisiteComparison.NotEqual;
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

            meets = Evaluate(unit.CreatureId, comparison, value);
            return true;
        }

        private static bool Evaluate(
            uint creatureId,
            PrerequisiteComparison comparison,
            uint value)
        {
            return comparison == PrerequisiteComparison.Equal
                ? creatureId == value
                : creatureId != value;
        }
    }
}
