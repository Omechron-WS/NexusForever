using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Combat;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Combat
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class CombatServiceProviderCollection
    {
        public const string Name = "Combat service provider";
    }

    [Collection(CombatServiceProviderCollection.Name)]
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

        [Fact]
        public void HandleEffectProc_RegistersConfiguredProcOnTarget()
        {
            var spell = new Mock<ISpell>();
            var target = new Mock<IUnitEntity>();
            var entry = new Spell4EffectsEntry
            {
                Id          = 789u,
                SpellId    = 123u,
                EffectType = SpellEffectType.Proc,
                DataBits00 = (uint)ProcType.BeginMoving,
                DataBits01 = 456u,
                DataBits02 = BitConverter.SingleToUInt32Bits(0.25f),
                DataBits04 = 250u
            };
            var info = new SpellTargetInfo.SpellTargetEffectInfo(1u, entry);
            IProcInfo appliedProc = null;
            target.Setup(t => t.ApplyProc(It.IsAny<IProcInfo>()))
                .Callback<IProcInfo>(proc => appliedProc = proc)
                .Returns(true);

            SpellHandler.HandleEffectProc(spell.Object, target.Object, info);

            target.Verify(t => t.ApplyProc(It.Is<IProcInfo>(proc =>
                ReferenceEquals(proc.Owner, target.Object)
                && proc.EffectId == 789u
                && proc.ApplicatorSpell4Id == 123u
                && proc.Type == ProcType.BeginMoving
                && proc.TriggerSpell4Id == 456u
                && proc.Chance == 0.25f)), Times.Once);
            spell.Verify(s => s.TrackProc(target.Object, appliedProc), Times.Once);
        }

        [Fact]
        public void HandleEffectProc_RejectedProcIsNotTrackedBySpell()
        {
            var spell = new Mock<ISpell>();
            var target = new Mock<IUnitEntity>();
            target.Setup(t => t.ApplyProc(It.IsAny<IProcInfo>())).Returns(false);
            var info = new SpellTargetInfo.SpellTargetEffectInfo(1u, new Spell4EffectsEntry
            {
                Id          = 789u,
                SpellId    = 123u,
                EffectType = SpellEffectType.Proc,
                DataBits00 = (uint)ProcType.BeginMoving,
                DataBits01 = 456u,
                DataBits02 = BitConverter.SingleToUInt32Bits(1f)
            });

            SpellHandler.HandleEffectProc(spell.Object, target.Object, info);

            spell.Verify(s => s.TrackProc(
                It.IsAny<IUnitEntity>(), It.IsAny<IProcInfo>()), Times.Never);
        }

        [Fact]
        public void HandleEffectDamage_OrdinaryServerSpellFiresCriticalProcAgainstVictimBeforeDamage()
        {
            var calculator = new Mock<IDamageCalculator>();
            var factory = new Mock<IFactory<IDamageCalculator>>();
            factory.Setup(f => f.Resolve()).Returns(calculator.Object);
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton(factory.Object)
                .BuildServiceProvider();
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            LegacyServiceProvider.Provider = provider;

            try
            {
                var caster = new Mock<IUnitEntity>();
                var target = new Mock<IUnitEntity>();
                target.Setup(t => t.CanAttack(caster.Object)).Returns(true);
                var spell = new Mock<ISpell>();
                spell.Setup(s => s.Caster).Returns(caster.Object);
                var parameters = new Mock<ISpellParameters>();
                parameters.SetupGet(p => p.UserInitiatedSpellCast).Returns(false);
                parameters.SetupGet(p => p.IsProcTriggered).Returns(false);
                spell.SetupGet(s => s.Parameters).Returns(parameters.Object);
                var damage = new Mock<IDamageDescription>();
                damage.Setup(d => d.CombatResult).Returns(CombatResult.Critical);
                var info = new Mock<ISpellTargetEffectInfo>();
                info.Setup(i => i.Damage).Returns(damage.Object);
                var calls = new List<string>();
                caster.Setup(c => c.FireProc(ProcType.CriticalDamage, target.Object))
                    .Callback(() => calls.Add("critical"));
                target.Setup(t => t.TakeDamage(caster.Object, damage.Object, true))
                    .Callback(() => calls.Add("damage"));

                SpellHandler.HandleEffectDamage(spell.Object, target.Object, info.Object);

                Assert.Equal(["critical", "damage"], calls);
                caster.Verify(c => c.FireProc(ProcType.CriticalDamage, target.Object), Times.Once);
                target.Verify(t => t.TakeDamage(caster.Object, damage.Object, true), Times.Once);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Fact]
        public void HandleEffectDamage_ProcOriginSuppressesCriticalAndFurtherDamageProcs()
        {
            var calculator = new Mock<IDamageCalculator>();
            var factory = new Mock<IFactory<IDamageCalculator>>();
            factory.Setup(f => f.Resolve()).Returns(calculator.Object);
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton(factory.Object)
                .BuildServiceProvider();
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            LegacyServiceProvider.Provider = provider;

            try
            {
                var caster = new Mock<IUnitEntity>();
                var target = new Mock<IUnitEntity>();
                target.Setup(t => t.CanAttack(caster.Object)).Returns(true);
                var parameters = new Mock<ISpellParameters>();
                parameters.SetupGet(p => p.IsProcTriggered).Returns(true);
                var spell = new Mock<ISpell>();
                spell.SetupGet(s => s.Caster).Returns(caster.Object);
                spell.SetupGet(s => s.Parameters).Returns(parameters.Object);
                var damage = new Mock<IDamageDescription>();
                damage.SetupGet(d => d.CombatResult).Returns(CombatResult.Critical);
                var info = new Mock<ISpellTargetEffectInfo>();
                info.SetupGet(i => i.Damage).Returns(damage.Object);

                SpellHandler.HandleEffectDamage(spell.Object, target.Object, info.Object);

                caster.Verify(c => c.FireProc(
                    It.IsAny<ProcType>(), It.IsAny<IUnitEntity>()), Times.Never);
                target.Verify(t => t.TakeDamage(caster.Object, damage.Object, false), Times.Once);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HandleEffectProxy_PropagatesProcOriginToChildSpell(bool isProcTriggered)
        {
            var parameters = new Mock<ISpellParameters>();
            parameters.SetupGet(p => p.IsProcTriggered).Returns(isProcTriggered);
            var spell = new Mock<ISpell>();
            spell.SetupGet(s => s.Parameters).Returns(parameters.Object);
            var target = new Mock<IUnitEntity>();
            var info = new SpellTargetInfo.SpellTargetEffectInfo(1u, new Spell4EffectsEntry
            {
                EffectType = SpellEffectType.Proxy,
                DataBits00 = 456u
            });

            SpellHandler.HandleEffectProxy(spell.Object, target.Object, info);

            target.Verify(t => t.CastSpell(
                456u,
                It.Is<ISpellParameters>(p => p.IsProcTriggered == isProcTriggered
                    && !p.UserInitiatedSpellCast)), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HandleEffectDamage_DroppedOrMissingDamage_DoesNotApplyDamage(bool dropEffect)
        {
            var calculator = new Mock<IDamageCalculator>();
            calculator.Setup(c => c.CalculateDamage(
                    It.IsAny<IUnitEntity>(),
                    It.IsAny<IUnitEntity>(),
                    It.IsAny<ISpell>(),
                    It.IsAny<ISpellTargetEffectInfo>()))
                .Callback<IUnitEntity, IUnitEntity, ISpell, ISpellTargetEffectInfo>(
                    (_, _, _, info) => info.DropEffect = dropEffect);
            var factory = new Mock<IFactory<IDamageCalculator>>();
            factory.Setup(f => f.Resolve()).Returns(calculator.Object);
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton(factory.Object)
                .BuildServiceProvider();
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            LegacyServiceProvider.Provider = provider;

            try
            {
                var caster = new Mock<IUnitEntity>();
                var target = new Mock<IUnitEntity>();
                target.Setup(t => t.CanAttack(caster.Object)).Returns(true);
                var spell = new Mock<ISpell>();
                spell.Setup(s => s.Caster).Returns(caster.Object);
                var info = new Mock<ISpellTargetEffectInfo>();
                info.SetupProperty(i => i.DropEffect, false);

                SpellHandler.HandleEffectDamage(spell.Object, target.Object, info.Object);

                caster.Verify(c => c.FireProc(
                    It.IsAny<ProcType>(), It.IsAny<IUnitEntity>()), Times.Never);
                target.Verify(t => t.FireProc(
                    It.IsAny<ProcType>(), It.IsAny<IUnitEntity>()), Times.Never);
                target.Verify(t => t.TakeDamage(
                    It.IsAny<IUnitEntity>(), It.IsAny<IDamageDescription>()), Times.Never);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Fact]
        public void HandleEffectHealShields_OverflowClampsToMaximumCapacity()
        {
            var calculator = new Mock<IDamageCalculator>();
            calculator.Setup(c => c.CalculateBaseAmount(
                    It.IsAny<IUnitEntity>(),
                    It.IsAny<IUnitEntity>(),
                    It.IsAny<ISpellTargetEffectInfo>()))
                .Returns(10u);
            var factory = new Mock<IFactory<IDamageCalculator>>();
            factory.Setup(f => f.Resolve()).Returns(calculator.Object);
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton(factory.Object)
                .BuildServiceProvider();
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            LegacyServiceProvider.Provider = provider;

            try
            {
                var caster = new Mock<IUnitEntity>();
                var target = new Mock<IUnitEntity>();
                target.Setup(t => t.Shield).Returns(uint.MaxValue - 5u);
                target.Setup(t => t.MaxShieldCapacity).Returns(uint.MaxValue);
                var spell = new Mock<ISpell>();
                spell.Setup(s => s.Caster).Returns(caster.Object);
                var info = new Mock<ISpellTargetEffectInfo>();

                SpellHandler.HandleEffectHealShields(spell.Object, target.Object, info.Object);

                target.VerifySet(t => t.Shield = uint.MaxValue, Times.Once);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }
    }
}
