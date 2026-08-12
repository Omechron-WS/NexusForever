using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Spell
{
    /// <summary>
    /// Restricts registered effect handlers to their authoritative build-16042 row shapes.
    /// </summary>
    internal static class SpellEffectSupportPolicy
    {
        internal const uint ModifySpellCooldownBaseId = 20684u;

        /// <summary>
        /// Return whether a registered handler supports the complete supplied effect row.
        /// </summary>
        public static bool IsSupported(
            Spell4EffectsEntry entry,
            Func<uint, bool> spellBaseExists)
        {
            if (entry == null)
                return false;

            if (entry.EffectType != SpellEffectType.ModifySpellCooldown)
                return true;

            if (spellBaseExists == null
                || entry.TargetFlags != (uint)SpellEffectTargetFlags.Caster
                || entry.DamageType != (DamageType)0u
                || entry.DelayTime != 0u
                || entry.TickTime != 0u
                || entry.DurationTime != 0u
                || entry.Flags != 0u
                || entry.DataBits00 != (uint)EffectModifySpellCooldownType.SpellBase
                || entry.DataBits01 != ModifySpellCooldownBaseId
                || entry.DataBits02 != 0u
                || entry.DataBits03 != 0u
                || entry.DataBits04 != 0u
                || entry.DataBits05 != 0u
                || entry.DataBits06 != 0u
                || entry.DataBits07 != 0u
                || entry.DataBits08 != 0u
                || entry.DataBits09 != 0u
                || entry.InnateCostPerTickType0 != 0u
                || entry.InnateCostPerTickType1 != 0u
                || entry.InnateCostPerTick0 != 0u
                || entry.InnateCostPerTick1 != 0u
                || entry.EmmComparison != 0u
                || entry.EmmValue != 0u
                || BitConverter.SingleToUInt32Bits(entry.ThreatMultiplier) != 0x3F800000u
                || entry.Spell4EffectGroupListId != 0u
                || entry.PrerequisiteIdCasterApply != 0u
                || entry.PrerequisiteIdTargetApply != 0u
                || entry.PrerequisiteIdCasterPersistence != 0u
                || entry.PrerequisiteIdTargetPersistence != 0u
                || entry.PrerequisiteIdTargetSuspend != 0u
                || entry.ParameterType is not { Length: 4 }
                || entry.ParameterType.Any(parameter => parameter != SpellEffectParameterType.None)
                || entry.ParameterValue is not { Length: 4 }
                || entry.ParameterValue.Any(parameter => BitConverter.SingleToUInt32Bits(parameter) != 0u)
                || entry.PhaseFlags != uint.MaxValue
                || entry.OrderIndex != 0u)
                return false;

            try
            {
                return spellBaseExists(ModifySpellCooldownBaseId);
            }
            catch
            {
                return false;
            }
        }
    }
}
