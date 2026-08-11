using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    [Collection(CombatServiceProviderCollection.Name)]
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

        [Fact]
        public void ExecutePhases_FiltersPhaseFlagsAndPreservesDelayedActivationPhase()
        {
            using var context = new SpellTimelineTestContext();
            Spell4EffectsEntry everyPhase = SpellTimelineTestContext.CreateEffect(
                1u,
                delayTime: 100u,
                phaseFlags: 1u);
            Spell4EffectsEntry phaseOne = SpellTimelineTestContext.CreateEffect(
                2u,
                delayTime: 100u,
                phaseFlags: 1u << 1);
            Spell4EffectsEntry phaseTwo = SpellTimelineTestContext.CreateEffect(
                3u,
                delayTime: 100u,
                phaseFlags: 1u << 2);
            var spell = new TestMultiphaseTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Multiphase,
                    new Spell4Entry { Id = 123u },
                    [everyPhase, phaseOne, phaseTwo]));
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecutePhase((byte)0u, true);
            spell.ExecutePhase((byte)1u, false);
            spell.ExecutePhase((byte)2u, false);
            spell.Update(0.1d);

            Assert.Equal(
                [1u, 1u, 2u, 1u, 3u],
                context.Invocations.Select(invocation => invocation.Effect.Entry.Id));
            Assert.Equal(
                [(sbyte)0, (sbyte)1, (sbyte)2],
                context.Packets
                    .OfType<ServerSpellGo>()
                    .Select(packet => packet.Phase));
            Assert.Equal(
                [1u, 1u, 2u, 1u, 3u],
                context.Packets
                    .OfType<Server07F8>()
                    .Select(packet => packet.Spell4EffectId));
        }

        [Fact]
        public void DelayedPhaseEffect_UsesOnlyTelegraphGeometryForCapturedPhase()
        {
            using var context = new SpellTimelineTestContext();
            var phaseOneTarget = new Mock<IUnitEntity>();
            phaseOneTarget.SetupGet(entity => entity.Guid).Returns(10u);
            phaseOneTarget.SetupGet(entity => entity.InWorld).Returns(true);
            phaseOneTarget.SetupGet(entity => entity.Map).Returns(context.Map.Object);
            var phaseTwoTarget = new Mock<IUnitEntity>();
            phaseTwoTarget.SetupGet(entity => entity.Guid).Returns(11u);
            phaseTwoTarget.SetupGet(entity => entity.InWorld).Returns(true);
            phaseTwoTarget.SetupGet(entity => entity.Map).Returns(context.Map.Object);

            var phaseOneTelegraph = new Mock<ITelegraph>();
            phaseOneTelegraph.SetupGet(value => value.TelegraphDamage).Returns(new TelegraphDamageEntry
            {
                Id         = 1u,
                PhaseFlags = 1u << 1
            });
            phaseOneTelegraph.Setup(value => value.GetTargets()).Returns([phaseOneTarget.Object]);
            var phaseTwoTelegraph = new Mock<ITelegraph>();
            phaseTwoTelegraph.SetupGet(value => value.TelegraphDamage).Returns(new TelegraphDamageEntry
            {
                Id         = 2u,
                PhaseFlags = 1u << 2
            });
            phaseTwoTelegraph.Setup(value => value.GetTargets()).Returns([phaseTwoTarget.Object]);

            Spell4EffectsEntry phaseOneEffect = SpellTimelineTestContext.CreateEffect(
                1u,
                delayTime: 100u,
                phaseFlags: 1u << 1);
            phaseOneEffect.TargetFlags = (uint)SpellEffectTargetFlags.Telegraph;
            Spell4EffectsEntry phaseTwoEffect = SpellTimelineTestContext.CreateEffect(
                2u,
                delayTime: 100u,
                phaseFlags: 1u << 2);
            phaseTwoEffect.TargetFlags = (uint)SpellEffectTargetFlags.Telegraph;
            var spell = new TestMultiphaseTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Multiphase,
                    new Spell4Entry { Id = 123u },
                    [phaseOneEffect, phaseTwoEffect]));
            spell.AddTelegraph(phaseOneTelegraph.Object);
            spell.AddTelegraph(phaseTwoTelegraph.Object);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecutePhase((byte)1u, true);
            spell.ExecutePhase((byte)2u, false);
            spell.Update(0.1d);

            Assert.Collection(
                context.Invocations,
                invocation =>
                {
                    Assert.Equal(phaseOneEffect.Id, invocation.Effect.Entry.Id);
                    Assert.Same(phaseOneTarget.Object, invocation.Target);
                },
                invocation =>
                {
                    Assert.Equal(phaseTwoEffect.Id, invocation.Effect.Entry.Id);
                    Assert.Same(phaseTwoTarget.Object, invocation.Target);
                });
            Assert.Equal(
                [1u, 2u],
                context.Packets
                    .OfType<ServerSpellGo>()
                    .SelectMany(packet => packet.TelegraphPositionData)
                    .Select(position => (uint)position.TelegraphId));
        }

        [Fact]
        public void InvalidPhase_FailsClosedBeforeTelegraphMaskShift()
        {
            using var context = new SpellTimelineTestContext();
            var telegraph = new Mock<ITelegraph>();
            telegraph.SetupGet(value => value.TelegraphDamage).Returns(new TelegraphDamageEntry
            {
                Id         = 1u,
                PhaseFlags = 1u
            });
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u);
            effect.TargetFlags = (uint)SpellEffectTargetFlags.Telegraph;
            var spell = new TestMultiphaseTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Multiphase,
                    new Spell4Entry { Id = 123u },
                    [effect]));
            spell.AddTelegraph(telegraph.Object);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecutePhase((byte)32u, true);

            Assert.Empty(context.Invocations);
            telegraph.Verify(value => value.GetTargets(), Times.Never);
            Assert.Empty(Assert.Single(
                context.Packets.OfType<ServerSpellGo>()).TelegraphPositionData);
        }

        private sealed class TestMultiphaseTimelineSpell : SpellMultiphase
        {
            public TestMultiphaseTimelineSpell(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            public void ExecutePhase(byte phase, bool consumeVitalCost)
            {
                currentPhase = phase;
                Execute(consumeVitalCost);
            }

            public void AddTelegraph(ITelegraph telegraph)
            {
                telegraphs.Add(telegraph);
            }

            public void SetStatus(SpellStatus value)
            {
                status = value;
            }
        }
    }
}
