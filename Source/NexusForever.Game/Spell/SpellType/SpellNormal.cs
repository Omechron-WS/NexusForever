using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    [SpellType(CastMethod.Normal)]
    public class SpellNormal : Spell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public SpellNormal(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        protected override bool IsCastingInternal()
        {
            return base.IsCastingInternal() && status == SpellStatus.Casting;
        }
    }
}
