using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Event;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;
using NexusForever.Network.Session;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    public class SpellBaseTests
    {
        [Fact]
        public void ISpell_HasLateUpdateMethod()
        {
            // Verify the interface exposes LateUpdate
            var type = typeof(ISpell);
            var method = type.GetMethod("LateUpdate");
            Assert.NotNull(method);
            Assert.Equal(typeof(void), method.ReturnType);
        }

        [Fact]
        public void ISpell_HasFinishMethod()
        {
            var type = typeof(ISpell);
            var method = type.GetMethod("Finish");
            Assert.NotNull(method);
        }

        [Fact]
        public void ISpell_HasIsFinishingProperty()
        {
            var type = typeof(ISpell);
            var prop = type.GetProperty("IsFinishing");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop.PropertyType);
        }

        [Fact]
        public void ISpell_HasIsWaitingProperty()
        {
            var type = typeof(ISpell);
            var prop = type.GetProperty("IsWaiting");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop.PropertyType);
        }

        [Fact]
        public void Spell_CastMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("Cast");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_CancelCastMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("CancelCast");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_UpdateMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("Update");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_LateUpdateMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("LateUpdate");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_FinishMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("Finish");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_HasProtectedCanFinishMethod()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod(
                "CanFinish",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
            Assert.True(method.IsFamily); // protected
        }

        [Fact]
        public void Spell_HasProtectedVirtualExecute()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod(
                "Execute",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
            Assert.True(method.IsFamily); // protected
        }

        [Fact]
        public void Spell_HasProtectedVirtualSelectTargets()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod(
                "SelectTargets",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
            Assert.True(method.IsFamily); // protected
        }

        [Fact]
        public void Spell_StatusFieldIsProtected()
        {
            var field = typeof(NexusForever.Game.Spell.Spell).GetField(
                "status",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field);
            Assert.True(field.IsFamily); // protected
        }
    }
}
