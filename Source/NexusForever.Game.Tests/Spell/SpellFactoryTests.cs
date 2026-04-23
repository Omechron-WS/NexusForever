using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Spell
{
    public class SpellFactoryTests
    {
        [Fact]
        public void SpellTypeAttribute_Exists()
        {
            var type = typeof(SpellTypeAttribute);
            Assert.NotNull(type);
            Assert.True(typeof(Attribute).IsAssignableFrom(type));
        }

        [Fact]
        public void SpellTypeAttribute_HasCastMethodProperty()
        {
            var prop = typeof(SpellTypeAttribute).GetProperty("CastMethod");
            Assert.NotNull(prop);
            Assert.Equal(typeof(CastMethod), prop.PropertyType);
        }

        [Fact]
        public void SpellNormal_Exists()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellNormal, NexusForever.Game");
            Assert.NotNull(type);
        }

        [Fact]
        public void SpellNormal_ExtendsSpell()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellNormal, NexusForever.Game");
            Assert.True(typeof(NexusForever.Game.Spell.Spell).IsAssignableFrom(type));
        }

        [Fact]
        public void SpellNormal_HasSpellTypeAttribute()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellNormal, NexusForever.Game");
            var attr = type.GetCustomAttributes(typeof(SpellTypeAttribute), false);
            Assert.Single(attr);
            Assert.Equal(CastMethod.Normal, ((SpellTypeAttribute)attr[0]).CastMethod);
        }

        [Fact]
        public void IGlobalSpellManager_HasNewSpellMethod()
        {
            var type = typeof(NexusForever.Game.Abstract.Spell.IGlobalSpellManager);
            var method = type.GetMethod("NewSpell");
            Assert.NotNull(method);
        }
    }
}
