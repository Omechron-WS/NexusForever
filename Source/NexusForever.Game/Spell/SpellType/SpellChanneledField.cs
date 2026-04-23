using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Spell.SpellType
{
    [SpellType(CastMethod.ChanneledField)]
    public class SpellChanneledField : Spell
    {
        public SpellChanneledField(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        // Channeled field uses default Spell behaviour for now.
        // Full implementation deferred until field-type channels are needed.
    }
}
