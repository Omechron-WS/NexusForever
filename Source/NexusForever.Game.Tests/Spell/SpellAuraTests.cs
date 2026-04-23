using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Spell
{
    public class SpellAuraTests
    {
        [Fact]
        public void SpellAura_Exists()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellAura, NexusForever.Game");
            Assert.NotNull(type);
        }

        [Fact]
        public void SpellAura_ExtendsSpell()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellAura, NexusForever.Game");
            Assert.True(typeof(NexusForever.Game.Spell.Spell).IsAssignableFrom(type));
        }

        [Fact]
        public void SpellAura_HasSpellTypeAttribute()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellAura, NexusForever.Game");
            var attr = type.GetCustomAttributes(typeof(NexusForever.Game.Spell.SpellTypeAttribute), false);
            Assert.Single(attr);
            Assert.Equal(CastMethod.Aura, ((NexusForever.Game.Spell.SpellTypeAttribute)attr[0]).CastMethod);
        }

        [Fact]
        public void SpellAura_OverridesCast()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellAura, NexusForever.Game");
            var method = type.GetMethod("Cast");
            Assert.NotNull(method);
            Assert.Equal(type, method.DeclaringType);
        }

        [Fact]
        public void SpellAura_OverridesUpdate()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellAura, NexusForever.Game");
            var method = type.GetMethod("Update");
            Assert.NotNull(method);
            Assert.Equal(type, method.DeclaringType);
        }

        [Fact]
        public void SpellAura_OverridesSelectTargets()
        {
            var type = Type.GetType("NexusForever.Game.Spell.SpellType.SpellAura, NexusForever.Game");
            var method = type.GetMethod("SelectTargets",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.Equal(type, method.DeclaringType);
        }

        [Fact]
        public void ISpellTargetInfo_HasTargetSelectionState()
        {
            var prop = typeof(ISpellTargetInfo).GetProperty("TargetSelectionState");
            Assert.NotNull(prop);
            Assert.Equal(typeof(TargetSelectionState), prop.PropertyType);
        }
    }
}
