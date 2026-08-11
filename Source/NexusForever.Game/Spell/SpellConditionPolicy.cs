using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Spell
{
    /// <summary>
    /// Evaluates the conservative build-16042 subset of spell-level unit conditions.
    /// </summary>
    internal static class SpellConditionPolicy
    {
        private const uint noConditionId = 0u;
        private const uint ordinaryNoOpId = 4u;
        private const uint casterCannotBeInCombatId = 48u;
        private const uint targetCannotBeDeadId1 = 3u;
        private const uint targetCannotBeDeadId2 = 8u;
        private const uint ccNoOpId = 2u;

        private const uint deadMask = 1u << 0;
        private const uint inCombatMask = 1u << 1;

        /// <summary>
        /// Attempt to evaluate a spell's complete condition tuple.
        /// </summary>
        /// <remarks>
        /// Unsupported tuples preserve legacy behaviour. Classification completes before any caster
        /// or target state is read so a supported component is never partially enforced.
        /// </remarks>
        public static bool TryCheck(
            IUnitEntity caster,
            ISpellInfo spellInfo,
            uint primaryTargetId,
            out CastResult result)
        {
            ArgumentNullException.ThrowIfNull(caster);
            ArgumentNullException.ThrowIfNull(spellInfo);
            ArgumentNullException.ThrowIfNull(spellInfo.Entry);

            result = CastResult.Ok;
            if (!HasSupportedTuple(spellInfo))
                return false;

            Spell4Entry entry = spellInfo.Entry;
            if (entry.Spell4ConditionsIdCaster == casterCannotBeInCombatId
                && caster.InCombat)
            {
                result = CastResult.CasterCannotBeInCombat;
                return true;
            }

            if (entry.Spell4ConditionsIdTarget is targetCannotBeDeadId1 or targetCannotBeDeadId2)
            {
                IUnitEntity target = primaryTargetId == 0u
                    ? caster
                    : caster.GetVisible<IWorldEntity>(primaryTargetId) as IUnitEntity;
                if (target == null)
                {
                    result = CastResult.NoTarget;
                    return true;
                }

                if (!target.IsAlive)
                    result = CastResult.TargetCannotBeDead;
            }

            return true;
        }

        private static bool HasSupportedTuple(ISpellInfo spellInfo)
        {
            Spell4Entry entry = spellInfo.Entry;
            if (!IsSupportedCasterCondition(
                    entry.Spell4ConditionsIdCaster,
                    spellInfo.CasterConditions)
                || !IsSupportedTargetCondition(
                    entry.Spell4ConditionsIdTarget,
                    spellInfo.TargetConditions)
                || !IsSupportedCCCondition(
                    entry.Spell4CCConditionsIdCaster,
                    spellInfo.CasterCCConditions)
                || !IsSupportedCCCondition(
                    entry.Spell4CCConditionsIdTarget,
                    spellInfo.TargetCCConditions))
                return false;

            return entry.Spell4ConditionsIdCaster == casterCannotBeInCombatId
                || entry.Spell4ConditionsIdTarget is targetCannotBeDeadId1 or targetCannotBeDeadId2;
        }

        private static bool IsSupportedCasterCondition(
            uint conditionId,
            Spell4ConditionsEntry condition)
        {
            return conditionId switch
            {
                noConditionId                 => condition == null,
                ordinaryNoOpId                => Matches(condition, ordinaryNoOpId, 0u, 0u),
                casterCannotBeInCombatId      => Matches(
                    condition,
                    casterCannotBeInCombatId,
                    inCombatMask,
                    0u),
                _ => false
            };
        }

        private static bool IsSupportedTargetCondition(
            uint conditionId,
            Spell4ConditionsEntry condition)
        {
            return conditionId switch
            {
                noConditionId            => condition == null,
                ordinaryNoOpId           => Matches(condition, ordinaryNoOpId, 0u, 0u),
                targetCannotBeDeadId1     => Matches(
                    condition,
                    targetCannotBeDeadId1,
                    deadMask,
                    0u),
                targetCannotBeDeadId2     => Matches(
                    condition,
                    targetCannotBeDeadId2,
                    deadMask,
                    0u),
                _ => false
            };
        }

        private static bool IsSupportedCCCondition(
            uint conditionId,
            Spell4CCConditionsEntry condition)
        {
            return conditionId switch
            {
                noConditionId => condition == null,
                ccNoOpId      => condition != null
                    && condition.Id == ccNoOpId
                    && condition.CcStateMask == 0u
                    && condition.CcStateFlagsRequired == 0u,
                _ => false
            };
        }

        private static bool Matches(
            Spell4ConditionsEntry condition,
            uint conditionId,
            uint mask,
            uint value)
        {
            return condition != null
                && condition.Id == conditionId
                && condition.ConditionMask == mask
                && condition.ConditionValue == value;
        }
    }
}
