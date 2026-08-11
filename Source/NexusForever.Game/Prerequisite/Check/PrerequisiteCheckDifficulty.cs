using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;
using NexusForever.Game.Static.Setting;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Evaluates the difficulty selected for a unit's current map instance.
    /// </summary>
    [PrerequisiteCheck(PrerequisiteType.Difficulty)]
    public class PrerequisiteCheckDifficulty : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckDifficulty> log;

        /// <summary>
        /// Creates a new map-difficulty prerequisite check.
        /// </summary>
        public PrerequisiteCheckDifficulty(
            ILogger<PrerequisiteCheckDifficulty> log)
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
            if (!CanEvaluate(comparison, value, objectId)
                || !TryGetDifficulty(player?.Map, out WorldDifficulty difficulty))
            {
                log.LogWarning(
                    $"Unsupported component shape or map state {comparison}, {value}, {objectId} for {PrerequisiteType.Difficulty}!");
                return false;
            }

            return difficulty == (WorldDifficulty)value;
        }

        /// <inheritdoc />
        public bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId)
        {
            return comparison == PrerequisiteComparison.Equal
                && objectId == 0u
                && value is (uint)WorldDifficulty.Normal or (uint)WorldDifficulty.Veteran;
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
            if (!CanEvaluate(comparison, value, objectId)
                || !TryGetDifficulty(unit?.Map, out WorldDifficulty difficulty))
                return false;

            meets = difficulty == (WorldDifficulty)value;
            return true;
        }

        private static bool TryGetDifficulty(
            IBaseMap map,
            out WorldDifficulty difficulty)
        {
            difficulty = map?.Difficulty ?? default;
            return map != null
                && difficulty is WorldDifficulty.Normal or WorldDifficulty.Veteran;
        }
    }
}
