using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Spell.SpellType
{
    /// <summary>
    /// Standard single-cast spell. Uses the base Spell behaviour unmodified.
    /// </summary>
    [SpellType(CastMethod.Normal)]
    public class SpellNormal : Spell
    {
        public SpellNormal(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }
    }
}
