using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Combat
{
    public class SpellEffectHandlerTests
    {
        [Fact]
        public void HandleEffectHeal_HandlerExists()
        {
            var methods = typeof(SpellHandler).GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(SpellEffectHandlerAttribute), false)
                    .Cast<SpellEffectHandlerAttribute>()
                    .Any(a => a.SpellEffectType == SpellEffectType.Heal));

            Assert.Single(methods);
        }

        [Fact]
        public void HandleEffectHealShields_HandlerExists()
        {
            var methods = typeof(SpellHandler).GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(SpellEffectHandlerAttribute), false)
                    .Cast<SpellEffectHandlerAttribute>()
                    .Any(a => a.SpellEffectType == SpellEffectType.HealShields));

            Assert.Single(methods);
        }
    }
}
