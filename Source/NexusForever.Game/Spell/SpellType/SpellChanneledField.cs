using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Spell.SpellType
{
    [SpellType(CastMethod.ChanneledField)]
    public class SpellChanneledField : SpellChanneled
    {
        public SpellChanneledField(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

    }
}
