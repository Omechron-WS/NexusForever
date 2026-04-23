using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Spell
{
    public class SpellThresholdTests
    {
        [Fact]
        public void SpellChargeRelease_Exists()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChargeRelease, NexusForever.Game");
            Assert.NotNull(type);
        }

        [Fact]
        public void SpellChargeRelease_HasSpellTypeAttribute()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChargeRelease, NexusForever.Game");
            var attr = type.GetCustomAttributes(typeof(NexusForever.Game.Spell.SpellTypeAttribute), false);
            Assert.Single(attr);
            Assert.Equal(CastMethod.ChargeRelease, ((NexusForever.Game.Spell.SpellTypeAttribute)attr[0]).CastMethod);
        }

        [Fact]
        public void SpellRapidTap_Exists()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellRapidTap, NexusForever.Game");
            Assert.NotNull(type);
        }

        [Fact]
        public void SpellRapidTap_HasSpellTypeAttribute()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellRapidTap, NexusForever.Game");
            var attr = type.GetCustomAttributes(typeof(NexusForever.Game.Spell.SpellTypeAttribute), false);
            Assert.Single(attr);
            Assert.Equal(CastMethod.RapidTap, ((NexusForever.Game.Spell.SpellTypeAttribute)attr[0]).CastMethod);
        }

        [Fact]
        public void SpellChargeRelease_ExtendsSpell()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellChargeRelease, NexusForever.Game");
            Assert.True(typeof(NexusForever.Game.Spell.Spell).IsAssignableFrom(type));
        }

        [Fact]
        public void SpellRapidTap_ExtendsSpell()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellRapidTap, NexusForever.Game");
            Assert.True(typeof(NexusForever.Game.Spell.Spell).IsAssignableFrom(type));
        }

        [Fact]
        public void SpellClientSideInteraction_Exists()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellClientSideInteraction, NexusForever.Game");
            Assert.NotNull(type);
        }

        [Fact]
        public void SpellClientSideInteraction_HasSpellTypeAttribute()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellClientSideInteraction, NexusForever.Game");
            var attr = type.GetCustomAttributes(typeof(NexusForever.Game.Spell.SpellTypeAttribute), false);
            Assert.Single(attr);
            Assert.Equal(CastMethod.ClientSideInteraction, ((NexusForever.Game.Spell.SpellTypeAttribute)attr[0]).CastMethod);
        }
    }
}
