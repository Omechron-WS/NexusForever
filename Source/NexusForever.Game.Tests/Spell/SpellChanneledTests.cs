using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Spell
{
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
    }
}
