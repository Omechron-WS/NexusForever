using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;

namespace NexusForever.Game.Abstract
{
    public interface IDamageCalculator
    {
        void CalculateDamage(IUnitEntity attacker, IUnitEntity victim, ISpell spell, ISpellTargetEffectInfo info);

        /// <summary>
        /// Calculate the base heal/damage amount from spell effect parameters without the combat pipeline.
        /// </summary>
        uint CalculateBaseAmount(IUnitEntity caster, IUnitEntity target, ISpellTargetEffectInfo info);
    }
}
