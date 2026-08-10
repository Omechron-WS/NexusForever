using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Combat;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Combat;
using NexusForever.GameTable.Model;
using Moq;

namespace NexusForever.Game.Tests.Combat
{
    public class ProcTests
    {
        [Fact]
        public void ProcType_EnumExists()
        {
            Assert.True(typeof(ProcType).IsEnum);
        }

        [Fact]
        public void ProcType_HasExpectedValues()
        {
            Assert.Equal(11, (int)ProcType.BeginMoving);
            Assert.Equal(145, (int)ProcType.CriticalDamage);
            Assert.Equal(214, (int)ProcType.StopsMoving);
        }

        [Fact]
        public void IProcInfo_InterfaceExists()
        {
            var type = Type.GetType("NexusForever.Game.Abstract.Combat.IProcInfo, NexusForever.Game.Abstract");
            Assert.NotNull(type);
            Assert.True(type.IsInterface);
        }

        [Fact]
        public void ProcInfo_ImplementsInterface()
        {
            var interfaceType = Type.GetType("NexusForever.Game.Abstract.Combat.IProcInfo, NexusForever.Game.Abstract");
            var implType = Type.GetType("NexusForever.Game.Combat.ProcInfo, NexusForever.Game");
            Assert.NotNull(implType);
            Assert.True(interfaceType.IsAssignableFrom(implType));
        }

        [Fact]
        public void Trigger_WaitsForConfiguredDelayBeforeCasting()
        {
            var owner = new Mock<IUnitEntity>();
            var proc = new ProcInfo(owner.Object, CreateEntry(delayMilliseconds: 1000u));

            bool triggered = proc.Trigger();
            proc.Update(0.5d);

            Assert.True(triggered);
            Assert.False(proc.CanTrigger);
            owner.Verify(o => o.CastSpell(
                It.IsAny<uint>(), It.IsAny<SpellParameters>()), Times.Never);

            proc.Update(0.5d);

            Assert.True(proc.CanTrigger);
            owner.Verify(o => o.CastSpell(
                456u,
                It.Is<SpellParameters>(p => !p.UserInitiatedSpellCast)), Times.Once);
        }

        [Fact]
        public void Trigger_WhileAlreadyPending_IsRejected()
        {
            var owner = new Mock<IUnitEntity>();
            var proc = new ProcInfo(owner.Object, CreateEntry(delayMilliseconds: 1000u));

            bool first = proc.Trigger();
            bool second = proc.Trigger();
            proc.Update(1d);

            Assert.True(first);
            Assert.False(second);
            owner.Verify(o => o.CastSpell(
                456u, It.IsAny<SpellParameters>()), Times.Once);
        }

        [Fact]
        public void ZeroDelay_StillCastsFromUpdateLoop()
        {
            var owner = new Mock<IUnitEntity>();
            var proc = new ProcInfo(owner.Object, CreateEntry(delayMilliseconds: 0u));

            proc.Trigger();

            owner.Verify(o => o.CastSpell(
                It.IsAny<uint>(), It.IsAny<SpellParameters>()), Times.Never);

            proc.Update(0d);

            owner.Verify(o => o.CastSpell(
                456u, It.IsAny<SpellParameters>()), Times.Once);
        }

        private static Spell4EffectsEntry CreateEntry(uint delayMilliseconds)
        {
            return new Spell4EffectsEntry
            {
                SpellId    = 123u,
                DataBits00 = (uint)ProcType.CriticalDamage,
                DataBits01 = 456u,
                DataBits04 = delayMilliseconds
            };
        }
    }
}
