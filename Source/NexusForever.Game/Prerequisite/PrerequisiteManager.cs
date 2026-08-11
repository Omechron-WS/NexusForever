using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Shared;

namespace NexusForever.Game.Prerequisite
{
    public sealed class PrerequisiteManager : Singleton<PrerequisiteManager>, IPrerequisiteManager
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteManager> log;
        private readonly IServiceProvider serviceProvider;
        private readonly IGameTableManager gameTableManager;
        private readonly IFactory<IPrerequisiteParameters> prerequisiteParametersFactory;

        public PrerequisiteManager(
            ILogger<PrerequisiteManager> log,
            IServiceProvider serviceProvider,
            IGameTableManager gameTableManager,
            IFactory<IPrerequisiteParameters> prerequisiteParametersFactory)
        {
            this.log                           = log;
            this.serviceProvider               = serviceProvider;
            this.gameTableManager              = gameTableManager;
            this.prerequisiteParametersFactory = prerequisiteParametersFactory;
        }

        #endregion

        /// <summary>
        /// Checks if <see cref="IPlayer"/> meets supplied prerequisite.
        /// </summary>
        public bool Meets(IPlayer player, uint prerequisiteId)
        {
            IPrerequisiteParameters parameters = prerequisiteParametersFactory.Resolve();
            return Meets(player, prerequisiteId, parameters);
        }

        /// <summary>
        /// Checks if <see cref="IPlayer"/> meets supplied prerequisite.
        /// </summary>
        public bool Meets(IPlayer player, uint prerequisiteId, IPrerequisiteParameters parameters)
        {
            PrerequisiteEntry entry = gameTableManager.Prerequisite.GetEntry(prerequisiteId);
            if (entry == null)
                throw new ArgumentException();

            switch (entry.Flags)
            {
                case EvaluationMode.EvaluateAND:
                    return MeetsEvaluateAnd(player, prerequisiteId, entry, parameters);
                case EvaluationMode.EvaluateOR:
                    return MeetsEvaluateOr(player, prerequisiteId, entry, parameters);
                default:
                    log.LogTrace($"Unhandled EvaluationMode {entry.Flags}");
                    return false;
            }
        }

        /// <inheritdoc />
        public bool CanEvaluateForUnit(uint prerequisiteId)
        {
            return TryResolveUnitChecks(prerequisiteId, out _, out _);
        }

        /// <inheritdoc />
        public bool TryMeets(IUnitEntity unit, uint prerequisiteId, out bool meets)
        {
            meets = false;
            if (unit == null
                || !TryResolveUnitChecks(
                    prerequisiteId,
                    out PrerequisiteEntry entry,
                    out IReadOnlyList<UnitPrerequisiteComponent> components))
                return false;

            bool result = entry.Flags == EvaluationMode.EvaluateAND;
            foreach (UnitPrerequisiteComponent component in components)
            {
                bool componentMeets;
                try
                {
                    if (!component.Check.TryMeets(
                        unit,
                        component.Comparison,
                        component.Value,
                        component.ObjectId,
                        out componentMeets))
                        return false;
                }
                catch
                {
                    return false;
                }

                // Do not short circuit. Every component must remain dynamically evaluable even
                // when an earlier AND/OR result has already determined the boolean outcome.
                if (entry.Flags == EvaluationMode.EvaluateAND)
                    result &= componentMeets;
                else
                    result |= componentMeets;
            }

            meets = result;
            return true;
        }

        private bool TryResolveUnitChecks(
            uint prerequisiteId,
            out PrerequisiteEntry entry,
            out IReadOnlyList<UnitPrerequisiteComponent> components)
        {
            entry = null;
            components = null;

            try
            {
                entry = gameTableManager.Prerequisite?.GetEntry(prerequisiteId);
                if (entry == null
                    || entry.Flags is not (EvaluationMode.EvaluateAND or EvaluationMode.EvaluateOR)
                    || entry.PrerequisiteTypeId == null
                    || entry.PrerequisiteComparisonId == null
                    || entry.Value == null
                    || entry.ObjectId == null
                    || entry.PrerequisiteTypeId.Length != 3
                    || entry.PrerequisiteTypeId.Length != entry.PrerequisiteComparisonId.Length
                    || entry.PrerequisiteTypeId.Length != entry.Value.Length
                    || entry.PrerequisiteTypeId.Length != entry.ObjectId.Length)
                    return false;

                var resolved = new List<UnitPrerequisiteComponent>();
                for (int i = 0; i < entry.PrerequisiteTypeId.Length; i++)
                {
                    PrerequisiteType type = entry.PrerequisiteTypeId[i];
                    if (type == PrerequisiteType.None)
                    {
                        if (entry.PrerequisiteComparisonId[i] != 0
                            || entry.Value[i] != 0u
                            || entry.ObjectId[i] != 0u)
                            return false;

                        continue;
                    }

                    IPrerequisiteCheck handler = serviceProvider.GetKeyedService<IPrerequisiteCheck>(type);
                    if (handler is not IUnitPrerequisiteCheck unitHandler)
                        return false;

                    PrerequisiteComparison comparison = entry.PrerequisiteComparisonId[i];
                    uint value = entry.Value[i];
                    uint objectId = entry.ObjectId[i];
                    if (!unitHandler.CanEvaluate(comparison, value, objectId))
                        return false;

                    resolved.Add(new UnitPrerequisiteComponent(
                        unitHandler,
                        comparison,
                        value,
                        objectId));
                }

                if (resolved.Count == 0)
                    return false;

                components = resolved;
                return true;
            }
            catch
            {
                entry = null;
                components = null;
                return false;
            }
        }

        private bool MeetsEvaluateAnd(IPlayer player, uint prerequisiteId, PrerequisiteEntry entry, IPrerequisiteParameters parameters)
        {
            for (int i = 0; i < entry.PrerequisiteTypeId.Length; i++)
            {
                PrerequisiteType type = entry.PrerequisiteTypeId[i];
                if (type == PrerequisiteType.None)
                    continue;

                PrerequisiteComparison comparison = entry.PrerequisiteComparisonId[i];
                if (!Meets(player, type, comparison, entry.Value[i], entry.ObjectId[i], parameters))
                {
                    log.LogTrace($"Player {player.Name} failed prerequisite AND check ({prerequisiteId}) {type}, {comparison}, {entry.Value[i]}, {entry.ObjectId[i]}");
                    return false;
                }
            }

            return true;
        }

        private bool MeetsEvaluateOr(IPlayer player, uint prerequisiteId, PrerequisiteEntry entry, IPrerequisiteParameters parameters)
        {
            for (int i = 0; i < entry.PrerequisiteTypeId.Length; i++)
            {
                PrerequisiteType type = entry.PrerequisiteTypeId[i];
                if (type == PrerequisiteType.None)
                    continue;

                if (Meets(player, type, entry.PrerequisiteComparisonId[i], entry.Value[i], entry.ObjectId[i], parameters))
                    return true;
            }

            log.LogTrace($"Player {player.Name} failed prerequisite OR check ({prerequisiteId})");
            return false;
        }

        private bool Meets(IPlayer player, PrerequisiteType type, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            IPrerequisiteCheck handler = serviceProvider.GetKeyedService<IPrerequisiteCheck>(type);
            if (handler == null)
            {
                log.LogWarning($"Unhandled PrerequisiteType {type}!");
                return false;
            }

            return handler.Meets(player, comparison, value, objectId, parameters);
        }

        private readonly record struct UnitPrerequisiteComponent(
            IUnitPrerequisiteCheck Check,
            PrerequisiteComparison Comparison,
            uint Value,
            uint ObjectId);
    }
}
