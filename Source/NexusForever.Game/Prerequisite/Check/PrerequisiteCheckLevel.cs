using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    [PrerequisiteCheck(PrerequisiteType.Level)]
    public class PrerequisiteCheckLevel : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckLevel> log;

        public PrerequisiteCheckLevel(
            ILogger<PrerequisiteCheckLevel> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IPlayer player, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return player.Level == value;
                case PrerequisiteComparison.NotEqual:
                    return player.Level != value;
                case PrerequisiteComparison.GreaterThan:
                    return player.Level > value;
                case PrerequisiteComparison.GreaterThanOrEqual:
                    return player.Level >= value;
                case PrerequisiteComparison.LessThan:
                    return player.Level < value;
                case PrerequisiteComparison.LessThanOrEqual:
                    return player.Level <= value;
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.Level}!");
                    return false;
            }
        }

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

            meets = comparison switch
            {
                PrerequisiteComparison.Equal              => unit.Level == value,
                PrerequisiteComparison.NotEqual           => unit.Level != value,
                PrerequisiteComparison.GreaterThan        => unit.Level > value,
                PrerequisiteComparison.GreaterThanOrEqual => unit.Level >= value,
                PrerequisiteComparison.LessThan           => unit.Level < value,
                PrerequisiteComparison.LessThanOrEqual    => unit.Level <= value,
                _                                         => false
            };
            return true;
        }
    }
}
