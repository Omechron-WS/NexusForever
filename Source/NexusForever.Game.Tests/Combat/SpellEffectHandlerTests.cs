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
                SpellId    = 123u,
                EffectType = SpellEffectType.Proc,
                DataBits00 = (uint)ProcType.BeginMoving,
                DataBits01 = 456u,
                DataBits04 = 250u
            };
            var info = new SpellTargetInfo.SpellTargetEffectInfo(1u, entry);

            SpellHandler.HandleEffectProc(spell.Object, target.Object, info);

            target.Verify(t => t.ApplyProc(It.Is<IProcInfo>(proc =>
                ReferenceEquals(proc.Owner, target.Object)
                && proc.ApplicatorSpell4Id == 123u
                && proc.Type == ProcType.BeginMoving
                && proc.TriggerSpell4Id == 456u)), Times.Once);
        }

        [Fact]
        public void HandleEffectDamage_CriticalResult_FiresCasterProc()
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
                var damage = new Mock<IDamageDescription>();
                damage.Setup(d => d.CombatResult).Returns(CombatResult.Critical);
                var info = new Mock<ISpellTargetEffectInfo>();
                info.Setup(i => i.Damage).Returns(damage.Object);

                SpellHandler.HandleEffectDamage(spell.Object, target.Object, info.Object);

                caster.Verify(c => c.FireProc(ProcType.CriticalDamage), Times.Once);
                target.Verify(t => t.TakeDamage(caster.Object, damage.Object), Times.Once);
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
