using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
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
            Assert.Equal(12, (int)ProcType.OnHit);
            Assert.Equal(16, (int)ProcType.OnDamageReceived);
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
        public void Trigger_DefersCastToUpdateAndCarriesEventTarget()
        {
            var owner = new Mock<IUnitEntity>();
            var primaryTarget = new Mock<IUnitEntity>();
            primaryTarget.SetupGet(unit => unit.Guid).Returns(987u);
            var proc = new ProcInfo(owner.Object, CreateEntry(cooldownMilliseconds: 1000u));

            bool triggered = proc.Trigger(primaryTarget.Object);

            Assert.True(triggered);
            Assert.False(proc.CanTrigger);
            owner.Verify(o => o.CastSpellTracked(
                It.IsAny<uint>(), It.IsAny<SpellParameters>()), Times.Never);

            proc.Update(0d);

            Assert.False(proc.CanTrigger);
            owner.Verify(o => o.CastSpellTracked(
                456u,
                It.Is<SpellParameters>(p => !p.UserInitiatedSpellCast
                    && p.PrimaryTargetId == 987u)), Times.Once);
        }

        [Fact]
        public void Trigger_ZeroCooldownDamageProcMarksCastToPreventSelfChain()
        {
            var owner = new Mock<IUnitEntity>();
            ISpellParameters castParameters = null;
            owner.Setup(o => o.CastSpellTracked(456u, It.IsAny<ISpellParameters>()))
                .Callback<uint, ISpellParameters>((_, parameters) => castParameters = parameters);
            var proc = new ProcInfo(
                owner.Object,
                CreateEntry(chance: 1f, cooldownMilliseconds: uint.MaxValue));

            Assert.True(proc.Trigger());

            proc.Update(0d);

            Assert.NotNull(castParameters);
            Assert.True(castParameters.IsProcTriggered);
            Assert.False(castParameters.UserInitiatedSpellCast);
            Assert.True(proc.CanTrigger);
        }

        [Fact]
        public void Trigger_WhileAlreadyPending_IsRejected()
        {
            var owner = new Mock<IUnitEntity>();
            var proc = new ProcInfo(owner.Object, CreateEntry());

            bool first = proc.Trigger();
            bool second = proc.Trigger();
            proc.Update(0d);

            Assert.True(first);
            Assert.False(second);
            owner.Verify(o => o.CastSpellTracked(
                456u, It.IsAny<SpellParameters>()), Times.Once);
        }

        [Fact]
        public void Cooldown_BlocksRetriggerUntilElapsed()
        {
            var owner = new Mock<IUnitEntity>();
            var proc = new ProcInfo(owner.Object, CreateEntry(cooldownMilliseconds: 1000u));

            Assert.True(proc.Trigger());
            proc.Update(0d);
            Assert.False(proc.Trigger());
            Assert.False(proc.CanTrigger);

            proc.Update(0.999d);
            Assert.False(proc.CanTrigger);
            Assert.False(proc.Trigger());

            proc.Update(0.01d);
            Assert.True(proc.CanTrigger);
            Assert.True(proc.Trigger());

            owner.Verify(o => o.CastSpellTracked(
                456u, It.IsAny<SpellParameters>()), Times.Once);
        }

        [Fact]
        public void MaximumCooldownSentinels_AllowRetriggerAfterDeferredCast()
        {
            var owner = new Mock<IUnitEntity>();
            uint[] sentinels = [int.MaxValue, uint.MaxValue];

            foreach (uint sentinel in sentinels)
            {
                var proc = new ProcInfo(owner.Object, CreateEntry(cooldownMilliseconds: sentinel));

                Assert.True(proc.Trigger());
                Assert.False(proc.CanTrigger);

                proc.Update(0d);

                Assert.True(proc.CanTrigger);
                Assert.True(proc.Trigger());
            }
        }

        [Fact]
        public void Trigger_ChanceRollBelowConfiguredChance_SchedulesCast()
        {
            var owner = new Mock<IUnitEntity>();
            var proc = new ProcInfo(owner.Object, CreateEntry(chance: 0.25f), () => 0.249d);

            Assert.True(proc.Trigger());

            proc.Update(0d);
            owner.Verify(o => o.CastSpellTracked(
                456u, It.IsAny<SpellParameters>()), Times.Once);
        }

        [Fact]
        public void Trigger_ChanceRollAtConfiguredChance_IsRejectedWithoutCooldown()
        {
            var owner = new Mock<IUnitEntity>();
            var rolls = new Queue<double>([0.25d, 0.1d]);
            var proc = new ProcInfo(
                owner.Object,
                CreateEntry(chance: 0.25f, cooldownMilliseconds: 1000u),
                rolls.Dequeue);

            Assert.False(proc.Trigger());
            Assert.True(proc.CanTrigger);
            Assert.True(proc.Trigger());

            proc.Update(0d);
            owner.Verify(o => o.CastSpellTracked(
                456u, It.IsAny<SpellParameters>()), Times.Once);
        }

        [Fact]
        public void Trigger_ZeroChanceFailsWithoutInvokingRandomSource()
        {
            var owner = new Mock<IUnitEntity>();
            var proc = new ProcInfo(
                owner.Object,
                CreateEntry(chance: 0f),
                () => throw new InvalidOperationException("Zero chance must not roll."));

            Assert.False(proc.Trigger());
            Assert.False(proc.CanTrigger);

            proc.Update(1d);
            owner.Verify(o => o.CastSpellTracked(
                It.IsAny<uint>(), It.IsAny<SpellParameters>()), Times.Never);
        }

        [Fact]
        public void Trigger_InvalidConfiguredChancesFailClosed()
        {
            var owner = new Mock<IUnitEntity>();
            float[] invalidChances = [float.NaN, float.NegativeInfinity, -0.1f, 1.1f, float.PositiveInfinity];

            foreach (float chance in invalidChances)
            {
                var proc = new ProcInfo(owner.Object, CreateEntry(chance: chance), () => 0d);

                Assert.False(proc.Trigger());
                Assert.False(proc.CanTrigger);
                proc.Update(1d);
            }

            owner.Verify(o => o.CastSpellTracked(
                It.IsAny<uint>(), It.IsAny<SpellParameters>()), Times.Never);
        }

        [Fact]
        public void Cancel_PendingTriggerPreventsCastAndCannotRetrigger()
        {
            var owner = new Mock<IUnitEntity>();
            var proc = new ProcInfo(owner.Object, CreateEntry(cooldownMilliseconds: 1000u));
            proc.Trigger();

            proc.Cancel();
            proc.Update(1d);

            Assert.False(proc.CanTrigger);
            Assert.False(proc.Trigger());
            owner.Verify(o => o.CastSpellTracked(
                It.IsAny<uint>(), It.IsAny<SpellParameters>()), Times.Never);
        }

        [Fact]
        public void Cancel_FinishesOnlySpellCreatedByThisProcAndIsIdempotent()
        {
            var owner = new Mock<IUnitEntity>();
            var triggeredSpell = new Mock<ISpell>();
            var unrelatedSpell = new Mock<ISpell>();
            owner.Setup(o => o.CastSpellTracked(456u, It.IsAny<SpellParameters>()))
                .Returns(triggeredSpell.Object);
            var proc = new ProcInfo(owner.Object, CreateEntry());
            proc.Trigger();
            proc.Update(0d);

            proc.Cancel();
            proc.Cancel();

            triggeredSpell.Verify(s => s.Finish(), Times.Once);
            unrelatedSpell.Verify(s => s.Finish(), Times.Never);
        }

        [Fact]
        public void Cancel_DoesNotFinishAlreadyFinishedTriggeredSpell()
        {
            var owner = new Mock<IUnitEntity>();
            var triggeredSpell = new Mock<ISpell>();
            triggeredSpell.Setup(s => s.IsFinished).Returns(true);
            owner.Setup(o => o.CastSpellTracked(456u, It.IsAny<SpellParameters>()))
                .Returns(triggeredSpell.Object);
            var proc = new ProcInfo(owner.Object, CreateEntry());
            proc.Trigger();
            proc.Update(0d);

            proc.Cancel();

            triggeredSpell.Verify(s => s.Finish(), Times.Never);
        }

        private static Spell4EffectsEntry CreateEntry(
            float chance = 1f,
            uint cooldownMilliseconds = uint.MaxValue)
        {
            return new Spell4EffectsEntry
            {
                Id         = 789u,
                SpellId    = 123u,
                DataBits00 = (uint)ProcType.CriticalDamage,
                DataBits01 = 456u,
                DataBits02 = BitConverter.SingleToUInt32Bits(chance),
                DataBits04 = cooldownMilliseconds
            };
        }
    }
}
