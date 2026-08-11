using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Entity;
using NexusForever.Game.Spell;
using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable.Model;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    [Collection(CombatServiceProviderCollection.Name)]
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

        [Fact]
        public void NonTickEffect_AppliesOncePerOccupancyAndReappliesAfterReentry()
        {
            using var context = new SpellTimelineTestContext();
            var firstTarget = new Mock<IUnitEntity>();
            firstTarget.SetupGet(entity => entity.Guid).Returns(10u);
            var secondTarget = new Mock<IUnitEntity>();
            secondTarget.SetupGet(entity => entity.Guid).Returns(11u);
            IUnitEntity visibleTarget = firstTarget.Object;
            context.Caster.Setup(entity => entity.GetVisible<IUnitEntity>(42u))
                .Returns(() => visibleTarget);

            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u);
            effect.TargetFlags = (uint)SpellEffectTargetFlags.Target;
            SpellParameters parameters = SpellTimelineTestContext.CreateParameters(
                CastMethod.Aura,
                new Spell4Entry
                {
                    Id            = 123u,
                    SpellDuration = 1_000u
                },
                [effect]);
            parameters.PrimaryTargetId = 42u;
            var spell = new SpellAura(context.Caster.Object, parameters);

            spell.Cast();
            spell.Update(0d);
            spell.Update(0.1d);
            visibleTarget = secondTarget.Object;
            spell.Update(0.1d);
            visibleTarget = firstTarget.Object;
            spell.Update(0.1d);

            Assert.Equal(
                [10u, 11u, 10u],
                context.Invocations.Select(invocation => invocation.Target.Guid));
        }

        [Fact]
        public void SameGuidReplacement_RemovesOldReferenceAndAppliesToNewReference()
        {
            using var context = new SpellTimelineTestContext();
            var firstTarget = new Mock<IUnitEntity>();
            firstTarget.SetupGet(entity => entity.Guid).Returns(10u);
            firstTarget.SetupGet(entity => entity.IsAlive).Returns(true);
            firstTarget.Setup(entity => entity.AddSpellModifierProperty(
                    It.IsAny<ISpellPropertyModifier>()))
                .Returns(true);
            firstTarget.Setup(entity => entity.RemoveSpellModifierProperty(
                    It.IsAny<Property>(),
                    It.IsAny<SpellEffectIdentity>()))
                .Returns(true);
            var replacementTarget = new Mock<IUnitEntity>();
            replacementTarget.SetupGet(entity => entity.Guid).Returns(10u);
            IUnitEntity visibleTarget = firstTarget.Object;
            context.Caster.Setup(entity => entity.GetVisible<IUnitEntity>(42u))
                .Returns(() => visibleTarget);

            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u);
            effect.TargetFlags = (uint)SpellEffectTargetFlags.Target;
            SpellParameters parameters = SpellTimelineTestContext.CreateParameters(
                CastMethod.Aura,
                new Spell4Entry
                {
                    Id            = 123u,
                    SpellDuration = 1_000u
                },
                [effect]);
            parameters.PrimaryTargetId = 42u;
            var spell = new SpellAura(context.Caster.Object, parameters);
            var firstProc = new Mock<IProcInfo>();
            firstProc.SetupGet(value => value.Owner).Returns(firstTarget.Object);
            spell.TrackProc(firstTarget.Object, firstProc.Object);
            var modifier = new SpellPropertyModifier(
                new SpellEffectIdentity(spell.CastingId, 123u, effect.Id),
                Property.Strength,
                1u,
                0f,
                5f,
                0f);
            Assert.True(spell.ApplyPropertyModifier(firstTarget.Object, modifier));

            spell.Cast();
            spell.Update(0d);
            spell.Update(0.1d);
            visibleTarget = replacementTarget.Object;
            spell.Update(0.1d);

            Assert.Collection(
                context.Invocations,
                invocation => Assert.Same(firstTarget.Object, invocation.Target),
                invocation => Assert.Same(replacementTarget.Object, invocation.Target));
            firstTarget.Verify(target => target.RemoveProc(firstProc.Object), Times.Once);
            firstTarget.Verify(target => target.RemoveSpellModifierProperty(
                Property.Strength,
                modifier.Identity), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NonTickEffect_DroppedOrThrowingAttemptIsNotRetried(bool throwFromHandler)
        {
            using var context = new SpellTimelineTestContext
            {
                Handler = (_, _, info) =>
                {
                    if (throwFromHandler)
                        throw new InvalidOperationException("Test aura effect failure.");

                    info.DropEffect = true;
                }
            };
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u);
            var spell = new SpellAura(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Aura,
                    new Spell4Entry
                    {
                        Id            = 123u,
                        SpellDuration = 1_000u
                    },
                    [effect]));

            spell.Cast();
            spell.Update(0d);
            spell.Update(0.1d);
            spell.Update(0.1d);
            spell.Update(0.1d);

            Assert.Equal(1, context.InvocationAttempts);
            Assert.Empty(context.Packets.OfType<NexusForever.Network.World.Message.Model.Server07F8>());
            Assert.Empty(Assert.Single(
                context.Packets.OfType<NexusForever.Network.World.Message.Model.ServerSpellGo>()).TargetInfoData);
        }

        [Fact]
        public void PeriodicEffect_UsesCentralTickCadenceInsteadOfAuraRefreshCadence()
        {
            using var context = new SpellTimelineTestContext();
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(
                1u,
                tickTime: 250u,
                durationTime: 500u);
            var spell = new SpellAura(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Aura,
                    new Spell4Entry
                    {
                        Id            = 123u,
                        SpellDuration = 1_000u
                    },
                    [effect]));

            spell.Cast();
            spell.Update(0d);
            spell.Update(0.2d);
            Assert.Empty(context.Invocations);

            spell.Update(0.05d);
            Assert.Single(context.Invocations);

            spell.Update(0.25d);
            Assert.Equal(2, context.Invocations.Count);
            Assert.False(spell.IsFinishing);
        }

        [Fact]
        public void DelayedEffect_UsesAuraMembershipAtActivationTime()
        {
            using var context = new SpellTimelineTestContext();
            var firstTarget = new Mock<IUnitEntity>();
            firstTarget.SetupGet(entity => entity.Guid).Returns(10u);
            var replacementTarget = new Mock<IUnitEntity>();
            replacementTarget.SetupGet(entity => entity.Guid).Returns(11u);
            IUnitEntity visibleTarget = firstTarget.Object;
            context.Caster.Setup(entity => entity.GetVisible<IUnitEntity>(42u))
                .Returns(() => visibleTarget);

            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u, delayTime: 100u);
            effect.TargetFlags = (uint)SpellEffectTargetFlags.Target;
            SpellParameters parameters = SpellTimelineTestContext.CreateParameters(
                CastMethod.Aura,
                new Spell4Entry
                {
                    Id            = 123u,
                    SpellDuration = 1_000u
                },
                [effect]);
            parameters.PrimaryTargetId = 42u;
            var spell = new SpellAura(context.Caster.Object, parameters);

            spell.Cast();
            spell.Update(0d);
            visibleTarget = replacementTarget.Object;
            spell.Update(0.1d);

            Assert.Same(replacementTarget.Object, Assert.Single(context.Invocations).Target);
        }
    }
}
