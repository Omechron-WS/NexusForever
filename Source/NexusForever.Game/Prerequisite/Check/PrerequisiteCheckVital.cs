using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    [PrerequisiteCheck(PrerequisiteType.Vital)]
    public class PrerequisiteCheckVital : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckVital> log;

        public PrerequisiteCheckVital(
            ILogger<PrerequisiteCheckVital> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IPlayer player, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            if (!player.TryGetVitalValue((Vital)objectId, out float current) || !float.IsFinite(current))
            {
                log.LogWarning($"Unsupported vital {objectId} for {PrerequisiteType.Vital}.");
                return false;
            }

            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return current == value;
                case PrerequisiteComparison.NotEqual:
                    return current != value;
                case PrerequisiteComparison.GreaterThanOrEqual:
                    return current >= value;
                case PrerequisiteComparison.GreaterThan:
                    return current > value;
                case PrerequisiteComparison.LessThanOrEqual:
                    return current <= value;
                case PrerequisiteComparison.LessThan:
                    return current < value;
                default:
                    log.LogWarning($"Unhandled {comparison} for {PrerequisiteType.Vital}!");
                    return false;
            }
        }

        public bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId)
        {
            double exactValue = value;
            double floatingPointValue = (float)value;
            return VitalDefinition.TryGet((Vital)objectId, out _)
                && exactValue.Equals(floatingPointValue)
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
            if (unit == null
                || !CanEvaluate(comparison, value, objectId)
                || !unit.TryGetVitalValue((Vital)objectId, out float current)
                || !float.IsFinite(current))
                return false;

            meets = comparison switch
            {
                PrerequisiteComparison.Equal              => current == value,
                PrerequisiteComparison.NotEqual           => current != value,
                PrerequisiteComparison.GreaterThanOrEqual => current >= value,
                PrerequisiteComparison.GreaterThan        => current > value,
                PrerequisiteComparison.LessThanOrEqual    => current <= value,
                PrerequisiteComparison.LessThan           => current < value,
                _                                         => false
            };
            return true;
        }
    }
}
