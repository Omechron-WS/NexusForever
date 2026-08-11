using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Tests.Spell
{
    [Collection(CombatServiceProviderCollection.Name)]
    public class SpellChanneledTests
    {
        [Fact]
        public void SpellChanneled_Exists()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChanneled, NexusForever.Game");
            Assert.NotNull(type);
        }

        [Fact]
        public void SpellChanneled_ExtendsSpell()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChanneled, NexusForever.Game");
            Assert.True(typeof(NexusForever.Game.Spell.Spell).IsAssignableFrom(type));
        }

        [Fact]
        public void SpellChanneled_HasSpellTypeAttribute()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChanneled, NexusForever.Game");
            var attr = type.GetCustomAttributes(typeof(NexusForever.Game.Spell.SpellTypeAttribute), false);
            Assert.Single(attr);
            Assert.Equal(CastMethod.Channeled, ((NexusForever.Game.Spell.SpellTypeAttribute)attr[0]).CastMethod);
        }

        [Fact]
        public void SpellChanneled_OverridesCast()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChanneled, NexusForever.Game");
            var method = type.GetMethod("Cast");
            Assert.NotNull(method);
            Assert.Equal(type, method.DeclaringType);
        }

        [Fact]
        public void SpellChanneledField_Exists()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChanneledField, NexusForever.Game");
            Assert.NotNull(type);
        }

        [Fact]
        public void SpellChanneledField_HasSpellTypeAttribute()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChanneledField, NexusForever.Game");
            var attr = type.GetCustomAttributes(typeof(NexusForever.Game.Spell.SpellTypeAttribute), false);
            Assert.Single(attr);
            Assert.Equal(CastMethod.ChanneledField, ((NexusForever.Game.Spell.SpellTypeAttribute)attr[0]).CastMethod);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ChannelRootPulses_CreateIndependentOverlappingEffectTimelines(bool field)
        {
            using var context = new SpellTimelineTestContext();
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(
                1u,
                tickTime: 100u,
                durationTime: 300u);
            CastMethod castMethod = field ? CastMethod.ChanneledField : CastMethod.Channeled;
            NexusForever.Game.Spell.Spell spell = field
                ? new SpellChanneledField(
                    context.Caster.Object,
                    SpellTimelineTestContext.CreateParameters(
                        castMethod,
                        CreateEntry(),
                        [effect]))
                : new SpellChanneled(
                    context.Caster.Object,
                    SpellTimelineTestContext.CreateParameters(
                        castMethod,
                        CreateEntry(),
                        [effect]));

            spell.Cast();
            spell.Update(0d);
            spell.Update(0.1d);
            spell.Update(0.1d);
            spell.Update(0.1d);

            Assert.Equal(6, context.Invocations.Count);
            Assert.Equal(6, context.Packets.OfType<NexusForever.Network.World.Message.Model.Server07F8>().Count());
            Assert.True(spell.IsFinishing);
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        private static Spell4Entry CreateEntry()
        {
            return new Spell4Entry
            {
                Id                  = 123u,
                ChannelInitialDelay = 0u,
                ChannelPulseTime    = 100u,
                ChannelMaxTime      = 300u
            };
        }
    }
}
