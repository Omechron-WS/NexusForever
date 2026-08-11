using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;

namespace NexusForever.Game.Prerequisite.Check
{
    [PrerequisiteCheck(PrerequisiteType.BaseFaction)]
    public class PrerequisiteCheckBaseFaction : IPrerequisiteCheck, IUnitPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckBaseFaction> log;
        private readonly IGameTableManager gameTableManager;

        public PrerequisiteCheckBaseFaction(
            ILogger<PrerequisiteCheckBaseFaction> log,
            IGameTableManager gameTableManager)
        {
            this.log              = log;
            this.gameTableManager = gameTableManager;
        }

        #endregion

        public bool Meets(IPlayer player, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return player.Faction1 == (Faction)value;
                case PrerequisiteComparison.NotEqual:
                    return player.Faction1 != (Faction)value;
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.BaseFaction}!");
                    return false;
            }
        }

        public bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId)
        {
            return objectId == 0u
                && (comparison is PrerequisiteComparison.Equal or PrerequisiteComparison.NotEqual)
                && gameTableManager.Faction2?.GetEntry(value) != null;
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
                PrerequisiteComparison.Equal    => unit.Faction1 == (Faction)value,
                PrerequisiteComparison.NotEqual => unit.Faction1 != (Faction)value,
                _                               => false
            };
            return true;
        }
    }
}
