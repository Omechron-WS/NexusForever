using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Spell
{
    /// <summary>
    /// Evaluates build-16042 spell vital requirements and atomically applies innate vital costs.
    /// </summary>
    internal static class SpellVitalPolicy
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Validate the caster innate vital requirements declared by a spell entry.
        /// </summary>
        public static CastResult CheckCasterRequirements(IUnitEntity caster, Spell4Entry entry)
        {
            ArgumentNullException.ThrowIfNull(caster);
            ArgumentNullException.ThrowIfNull(entry);

            CastResult result = CheckCasterRequirement(
                caster,
                entry.CasterInnateRequirement0,
                entry.CasterInnateRequirementValue0,
                entry.CasterInnateRequirementEval0);
            if (result != CastResult.Ok)
                return result;

            return CheckCasterRequirement(
                caster,
                entry.CasterInnateRequirement1,
                entry.CasterInnateRequirementValue1,
                entry.CasterInnateRequirementEval1);
        }

        /// <summary>
        /// Validate the target-begin innate vital requirement declared by a spell entry.
        /// </summary>
        public static CastResult CheckTargetRequirement(IUnitEntity target, Spell4Entry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (entry.TargetBeginInnateRequirement == 0u)
                return CastResult.Ok;

            if (target == null)
                return CastResult.TargetUnknown;

            if (!TryMeetsRequirement(
                    target,
                    entry.TargetBeginInnateRequirement,
                    entry.TargetBeginInnateRequirementValue,
                    entry.TargetBeginInnateRequirementEval,
                    out bool meets))
                return CastResult.SpellBad;

            return meets ? CastResult.Ok : CastResult.TargetVital;
        }

        /// <summary>
        /// Validate that an entity has enough of every innate vital cost without modifying it.
        /// </summary>
        public static CastResult CheckCosts(IUnitEntity entity, Spell4Entry entry)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(entry);

            CastResult result = TryBuildCosts(entry, out List<VitalCost> costs);
            if (result != CastResult.Ok)
                return result;

            return CheckCosts(entity, costs);
        }

        /// <summary>
        /// Return whether a spell entry declares a non-zero innate vital cost.
        /// </summary>
        public static bool HasCost(Spell4Entry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            return entry.InnateCost0 != 0u || entry.InnateCost1 != 0u;
        }

        /// <summary>
        /// Return whether a threshold entry declares a non-zero vital cost.
        /// </summary>
        public static bool HasCost(Spell4ThresholdsEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            return entry.VitalCostValue00 != 0u || entry.VitalCostValue01 != 0u;
        }

        /// <summary>
        /// Validate and consume all innate vital costs as one world-thread transaction.
        /// </summary>
        /// <remarks>
        /// Costs which alias the same backing stat are aggregated before validation. Every cost is
        /// preflighted before the first mutation. The game map serialises spell execution on its world
        /// thread; rollback therefore only protects against an unexpected vital implementation failure.
        /// A mutation exception is reconciled against the backing vital because entity setters update
        /// storage before broadcasting. Health is applied last because its zero transition has
        /// irreversible death-lifecycle effects; an exception while reaching zero fails closed because
        /// the death transition cannot safely be inferred through <see cref="IUnitEntity"/>.
        /// </remarks>
        public static CastResult TryConsumeCosts(IUnitEntity entity, Spell4Entry entry)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(entry);

            CastResult result = TryBuildCosts(entry, out List<VitalCost> costs);
            if (result != CastResult.Ok)
                return result;

            result = CheckCosts(entity, costs);
            if (result != CastResult.Ok)
                return result;

            var applied = new List<VitalCost>(costs.Count);
            foreach (VitalCost cost in costs.OrderBy(cost => cost.Stat == Stat.Health))
            {
                if (TryApplyCost(entity, cost))
                {
                    applied.Add(cost);
                    continue;
                }

                RollBack(entity, applied);
                return CastResult.SpellBad;
            }

            return CastResult.Ok;
        }

        private static bool TryApplyCost(IUnitEntity entity, VitalCost cost)
        {
            if (!entity.TryGetVitalValue(cost.Vital, out float previous) || !float.IsFinite(previous))
            {
                log.Error($"Failed to re-read preflighted spell vital cost {cost.Vital} before mutation.");
                return false;
            }

            float amount = (float)cost.Amount;
            float expected = CalculateExpectedValue(cost, previous, amount);
            try
            {
                if (entity.TryModifyVital(cost.Vital, -amount))
                    return true;

                log.Error($"Failed to apply preflighted spell vital cost {cost.Amount} for {cost.Vital}.");
                return false;
            }
            catch (Exception exception)
            {
                if (!entity.TryGetVitalValue(cost.Vital, out float current)
                    || !float.IsFinite(current)
                    || current != expected)
                {
                    log.Error(exception, $"Spell vital cost {cost.Amount} for {cost.Vital} failed before its expected mutation committed.");
                    return false;
                }

                if (cost.Stat == Stat.Health && expected == 0f)
                {
                    log.Error(exception, "A spell health cost reached zero while its entity notification failed; the death transition cannot be reconciled safely.");
                    return false;
                }

                log.Error(exception, $"Spell vital cost {cost.Amount} for {cost.Vital} committed but its entity notification failed.");
                return true;
            }
        }

        private static float CalculateExpectedValue(VitalCost cost, float current, float amount)
        {
            VitalDefinition.TryGet(cost.Vital, out VitalDefinition definition);
            double expected = Math.Max((double)current - amount, 0d);
            return definition.UsesIntegerStorage
                ? (float)Math.Truncate(expected)
                : (float)expected;
        }

        /// <summary>
        /// Evaluate a build-16042 innate vital requirement.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when the vital and evaluation mode are supported; otherwise,
        /// <see langword="false"/>. A supported comparison result is returned through <paramref name="meets"/>.
        /// </returns>
        public static bool TryMeetsRequirement(
            IUnitEntity entity,
            uint vitalValue,
            uint requirementValue,
            uint evaluation,
            out bool meets)
        {
            ArgumentNullException.ThrowIfNull(entity);

            meets = false;
            var vital = (Vital)vitalValue;
            if (!entity.TryGetVitalValue(vital, out float current) || !float.IsFinite(current))
            {
                log.Warn($"Unsupported spell innate vital requirement {vitalValue}.");
                return false;
            }

            double currentValue = current;
            switch (evaluation)
            {
                case 0u:
                    meets = currentValue == requirementValue;
                    return true;
                case 1u:
                    meets = currentValue != requirementValue;
                    return true;
                case 2u:
                    meets = currentValue >= requirementValue;
                    return true;
                case 3u:
                    meets = currentValue > requirementValue;
                    return true;
                case 4u:
                    meets = currentValue <= requirementValue;
                    return true;
                case 5u:
                    meets = currentValue < requirementValue;
                    return true;
                case 6u:
                case 7u:
                case 8u:
                case 9u:
                    if (!entity.TryGetVitalMaximum(vital, out float maximum)
                        || !float.IsFinite(maximum)
                        || maximum <= 0f)
                    {
                        log.Warn($"Spell innate percentage requirement uses unbounded vital {vitalValue}.");
                        return false;
                    }

                    double percentageValue = currentValue * 100d;
                    double percentageRequirement = (double)maximum * requirementValue;
                    meets = evaluation switch
                    {
                        6u => percentageValue >= percentageRequirement,
                        7u => percentageValue > percentageRequirement,
                        8u => percentageValue <= percentageRequirement,
                        9u => percentageValue < percentageRequirement,
                        _  => false
                    };
                    return true;
                default:
                    log.Warn($"Unsupported spell innate vital evaluation {evaluation}.");
                    return false;
            }
        }

        /// <summary>
        /// Return the build-16042 failure result associated with a supported caster vital.
        /// </summary>
        public static CastResult GetFailureResult(Vital vital)
        {
            return vital switch
            {
                Vital.Health                                                => CastResult.CasterVitalCostHealth,
                Vital.Resource0                                             => CastResult.CasterVitalCostResource0,
                Vital.Resource1 or Vital.KineticCell or Vital.StalkerB
                    or Vital.MedicCore or Vital.Volatility                   => CastResult.CasterVitalCostResource1,
                Vital.Resource2                                             => CastResult.CasterVitalCostResource2,
                Vital.Resource3 or Vital.StalkerA                           => CastResult.CasterVitalCostResource3,
                Vital.Resource4 or Vital.SpellSurge                         => CastResult.CasterVitalCostResource4,
                Vital.Resource5                                             => CastResult.CasterVitalCostResource5,
                Vital.Resource6                                             => CastResult.CasterVitalCostResource6,
                Vital.Focus                                                 => CastResult.CasterVitalCostFocus,
                Vital.ShieldCapacity                                        => CastResult.CasterVitalCostShieldCapacity,
                Vital.Resource7                                             => CastResult.CasterVitalCostResource7,
                Vital.InterruptArmor                                        => CastResult.CasterVitalCostInterruptArmor,
                _                                                           => CastResult.SpellBad
            };
        }

        private static CastResult CheckCasterRequirement(
            IUnitEntity caster,
            uint vitalValue,
            uint requirementValue,
            uint evaluation)
        {
            if (vitalValue == 0u)
                return CastResult.Ok;

            if (!TryMeetsRequirement(caster, vitalValue, requirementValue, evaluation, out bool meets))
                return CastResult.SpellBad;

            return meets ? CastResult.Ok : GetFailureResult((Vital)vitalValue);
        }

        private static CastResult TryBuildCosts(Spell4Entry entry, out List<VitalCost> costs)
        {
            costs = [];

            CastResult result = AddCost(costs, entry.InnateCostType0, entry.InnateCost0);
            if (result != CastResult.Ok)
                return result;

            return AddCost(costs, entry.InnateCostType1, entry.InnateCost1);
        }

        private static CastResult AddCost(List<VitalCost> costs, uint vitalValue, uint amount)
        {
            // Build 16042 contains explicit vital identifiers with a zero amount. They are metadata,
            // not a resource transaction, and must not make an otherwise valid cast fail closed.
            if (amount == 0u)
                return CastResult.Ok;

            if (vitalValue == 0u)
            {
                log.Warn($"Spell innate vital cost has amount {amount} without a vital identifier.");
                return CastResult.SpellBad;
            }

            var vital = (Vital)vitalValue;
            if (!VitalDefinition.TryGet(vital, out VitalDefinition definition))
            {
                log.Warn($"Unsupported spell innate vital cost {vitalValue}.");
                return CastResult.SpellBad;
            }

            int existingIndex = costs.FindIndex(cost => cost.Stat == definition.Stat);
            if (existingIndex >= 0)
            {
                VitalCost existing = costs[existingIndex];
                costs[existingIndex] = existing with { Amount = existing.Amount + amount };
            }
            else
                costs.Add(new VitalCost(vital, definition.Stat, amount));

            return CastResult.Ok;
        }

        private static CastResult CheckCosts(IUnitEntity entity, List<VitalCost> costs)
        {
            foreach (VitalCost cost in costs)
            {
                if (!entity.TryGetVitalValue(cost.Vital, out float current) || !float.IsFinite(current))
                    return CastResult.SpellBad;

                if ((double)current < cost.Amount)
                    return GetFailureResult(cost.Vital);
            }

            return CastResult.Ok;
        }

        private static void RollBack(IUnitEntity entity, List<VitalCost> costs)
        {
            for (int i = costs.Count - 1; i >= 0; i--)
            {
                VitalCost cost = costs[i];
                try
                {
                    if (!entity.TryModifyVital(cost.Vital, (float)cost.Amount))
                        log.Error($"Failed to roll back spell vital cost {cost.Amount} for {cost.Vital}.");
                }
                catch (Exception exception)
                {
                    // Rollback is best-effort and must never prevent the spell failure lifecycle from
                    // completing. The original mutation failure is already reported by the caller.
                    log.Error(exception, $"Spell vital cost rollback failed for {cost.Vital}.");
                }
            }
        }

        private readonly record struct VitalCost(Vital Vital, Stat Stat, double Amount);
    }
}
