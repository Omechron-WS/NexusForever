using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    [PrerequisiteCheck(PrerequisiteType.Vital)]
    public class PrerequisiteCheckVital : IPrerequisiteCheck
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
    }
}
