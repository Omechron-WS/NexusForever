using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Spell
{
    public class SpellMultiphaseTests
    {
        [Fact]
        public void SpellMultiphase_Exists()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellMultiphase, NexusForever.Game");
            Assert.NotNull(type);
        }

        [Fact]
        public void SpellMultiphase_ExtendsSpell()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellMultiphase, NexusForever.Game");
            Assert.True(typeof(NexusForever.Game.Spell.Spell).IsAssignableFrom(type));
        }

        [Fact]
        public void SpellMultiphase_HasSpellTypeAttribute()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellMultiphase, NexusForever.Game");
            var attr = type.GetCustomAttributes(typeof(NexusForever.Game.Spell.SpellTypeAttribute), false);
            Assert.Single(attr);
            Assert.Equal(CastMethod.Multiphase, ((NexusForever.Game.Spell.SpellTypeAttribute)attr[0]).CastMethod);
        }

        [Fact]
        public void SpellMultiphase_OverridesCast()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellMultiphase, NexusForever.Game");
            var method = type.GetMethod("Cast");
            Assert.NotNull(method);
            Assert.Equal(type, method.DeclaringType);
        }
    }
}
