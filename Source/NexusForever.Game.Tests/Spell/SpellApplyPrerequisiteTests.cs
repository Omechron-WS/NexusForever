using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Entity;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Spell;
using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Prerequisite;
using NexusForever.Game.Static.Reputation;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Network.World.Message.Static;
using NexusForever.Script;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    [Collection(CombatServiceProviderCollection.Name)]
    public class SpellApplyPrerequisiteTests
    {
        [Fact]
        public void CasterAndTargetGates_FilterBeforeEffectCostAndHandler()
        {
            PrerequisiteEntry casterPrerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            PrerequisiteEntry targetPrerequisite = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.BaseFaction, PrerequisiteComparison.Equal, (uint)Faction.Exile, 0u));
            using var context = new SpellPrerequisiteContext(casterPrerequisite, targetPrerequisite)
            {
                CasterLevel = 10u
            };
            Mock<IUnitEntity> eligible = context.CreateTarget(10u, faction: () => Faction.Exile);
            Mock<IUnitEntity> rejected = context.CreateTarget(11u, faction: () => Faction.Dominion);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                casterPrerequisite: casterPrerequisite.Id,
                targetPrerequisite: targetPrerequisite.Id,
                cost: 10u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, eligible.Object),
                (SpellEffectTargetFlags.Target, rejected.Object));

            spell.ExecuteForTest();

            Assert.Equal(90f, context.CasterResource1);
            Assert.Equal([-10f], context.CostMutations);
            Assert.Same(eligible.Object, Assert.Single(context.Invocations).Target);
            TargetInfo targetInfo = Assert.Single(
                Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData);
            Assert.Equal(eligible.Object.Guid, targetInfo.UnitId);
            Assert.Empty(context.Packets.OfType<Server07F8>());
        }

        [Fact]
        public void SupportedFalseImmediateGate_EmitsNoCostHandlerOrEffectMetadata()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(10u, level: () => 9u);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id,
                cost: 10u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();

            Assert.Equal(100f, context.CasterResource1);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<Server07F8>());
            Assert.Empty(Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData);
        }

        [Fact]
        public void MixedPlayerOnlyRow_NeverRegistersOrAdvertises()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u),
                (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(10u, level: () => 50u);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id,
                delayTime: 100u,
                cost: 10u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();
            spell.Update(1d);

            Assert.Equal(100f, context.CasterResource1);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<Server07F8>());
            Assert.Empty(Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void PersistenceOrSuspendPrerequisite_RemainsGated(int field)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            Spell4EffectsEntry effect = CreateEffect(1u, delayTime: 100u, cost: 10u);
            switch (field)
            {
                case 0:
                    effect.PrerequisiteIdCasterPersistence = 1u;
                    break;
                case 1:
                    effect.PrerequisiteIdTargetPersistence = 1u;
                    break;
                case 2:
                    effect.PrerequisiteIdTargetSuspend = 1u;
                    break;
            }

            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();
            spell.Update(1d);

            Assert.Equal(100f, context.CasterResource1);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<Server07F8>());
            Assert.Empty(Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData);
        }

        [Theory]
        [InlineData(true, false, 1)]
        [InlineData(false, false, 1)]
        [InlineData(false, true, 2)]
        public void DelayedOrTickingGate_ReevaluatesCasterAndTargetAtDueTime(
            bool casterPrerequisite,
            bool ticking,
            int expectedInvocations)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterLevel = casterPrerequisite ? 9u : 50u
            };
            uint targetLevel = 9u;
            Mock<IUnitEntity> target = context.CreateTarget(10u, level: () => targetLevel);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                casterPrerequisite: casterPrerequisite ? prerequisite.Id : 0u,
                targetPrerequisite: casterPrerequisite ? 0u : prerequisite.Id,
                delayTime: ticking ? 0u : 100u,
                tickTime: ticking ? 100u : 0u,
                durationTime: ticking ? 300u : 0u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();
            Assert.Empty(context.Invocations);
            Assert.Single(Assert.Single(
                context.Packets.OfType<ServerSpellGo>()).TargetInfoData);

            if (ticking)
            {
                spell.Update(0.1d);
                Assert.Empty(context.Invocations);
                targetLevel = 10u;
                spell.Update(0.2d);
            }
            else
            {
                if (casterPrerequisite)
                    context.CasterLevel = 10u;
                else
                    targetLevel = 10u;
                spell.Update(0.1d);
            }

            Assert.Equal(expectedInvocations, context.Invocations.Count);
            Assert.Equal(expectedInvocations, context.Packets.OfType<Server07F8>().Count());
        }

        [Fact]
        public void PeriodicPrerequisiteReadFailure_IsContainedAndNeverChargesOrActivates()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Vital, PrerequisiteComparison.GreaterThan, 0u, (uint)Vital.Resource1));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                vital: _ => throw new InvalidOperationException("Test prerequisite read failure."));
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id,
                tickTime: 100u,
                durationTime: 300u,
                cost: 10u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            Exception exception = Record.Exception(() =>
            {
                spell.ExecuteForTest();
                spell.Update(0.3d);
            });

            Assert.Null(exception);
            Assert.Equal(100f, context.CasterResource1);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<Server07F8>());
        }

        [Fact]
        public void AuraFalseNonTickGate_RetriesUntilEligibleThenAppliesOncePerOccupancy()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            uint targetLevel = 9u;
            Mock<IUnitEntity> target = context.CreateTarget(42u, level: () => targetLevel);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id);
            TestApplyPrerequisiteAura spell = context.CreateAura(effect, target.Object);

            spell.ExecuteForTest();
            spell.Update(0.1d);
            Assert.Equal(0, context.InvocationAttempts);

            targetLevel = 10u;
            spell.Update(0.1d);
            spell.Update(0.1d);
            spell.Update(0.1d);

            Assert.Equal(1, context.InvocationAttempts);
            Assert.Single(context.Packets.OfType<Server07F8>());
        }

        [Fact]
        public void AuraThrowingAttempt_RetainsAttemptedOccupancySemantics()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                Handler = (_, _, _) => throw new InvalidOperationException("Test aura handler failure.")
            };
            Mock<IUnitEntity> target = context.CreateTarget(42u, level: () => 10u);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id);
            TestApplyPrerequisiteAura spell = context.CreateAura(effect, target.Object);

            spell.ExecuteForTest();
            spell.Update(0.1d);
            spell.Update(0.1d);

            Assert.Equal(1, context.InvocationAttempts);
            Assert.Empty(context.Packets.OfType<Server07F8>());
            Assert.Empty(Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData);
        }

        [Fact]
        public void PersistenceFalseBeforeStart_FailsWithoutPublishingSpellLifetime()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterLevel = 9u
            };
            Spell4EffectsEntry effect = CreateEffect(1u, cost: 10u);
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                effect,
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);

            spell.Cast();
            spell.Update(0d);
            spell.LateUpdate(0d);

            Assert.True(spell.IsFinished);
            ServerSpellCastResult result = Assert.Single(context.SessionPackets.OfType<ServerSpellCastResult>());
            Assert.Equal(CastResult.PrereqCasterPersistence, result.CastResult);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
            Assert.Empty(context.Packets.OfType<ServerSpellFinish>());
        }

        [Fact]
        public void PersistenceClassificationException_FailsClosedBeforeStart()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u),
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);
            IServiceProvider provider = LegacyServiceProvider.Provider;

            try
            {
                LegacyServiceProvider.Provider = new ThrowingPrerequisiteServiceProvider(provider);
                Exception exception = Record.Exception(spell.Cast);

                Assert.Null(exception);
                Assert.True(spell.IsFinishing);
                ServerSpellCastResult result = Assert.Single(context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Equal(CastResult.PrereqCasterPersistence, result.CastResult);
                Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            }
            finally
            {
                LegacyServiceProvider.Provider = provider;
            }
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-0.001d)]
        public void InvalidDelta_PerformsNoPersistenceOrTimelineWork(double delta)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u),
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);
            spell.Cast();
            int readsAfterCast = context.CasterLevelReads;

            spell.Update(delta);

            Assert.True(spell.IsCasting);
            Assert.Equal(readsAfterCast, context.CasterLevelReads);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
        }

        [Fact]
        public void ZeroDelta_IsValidAndExecutesDueWork()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u),
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);
            spell.Cast();

            spell.Update(0d);

            Assert.Single(context.Invocations);
            Assert.Single(context.Packets.OfType<ServerSpellGo>());
        }

        [Fact]
        public void UnsupportedPersistenceRow_PreservesLegacyLifetime()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u),
                (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u),
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);

            spell.Cast();
            spell.Update(0d);

            Assert.False(spell.IsFinishing);
            Assert.Single(context.Invocations);
            Assert.Single(context.Packets.OfType<ServerSpellGo>());
        }

        [Fact]
        public void SupportedPersistenceReadFailure_IsContainedAndFailsClosedOnce()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Vital, PrerequisiteComparison.GreaterThan, 25u, (uint)Vital.Resource0));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterResource0 = 50f
            };
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u),
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);
            spell.Cast();
            context.ThrowOnCasterVitalRead = true;

            Exception exception = Record.Exception(() => spell.Update(0d));
            int readsAfterFailure = context.CasterVitalReads;
            spell.Update(0d);

            Assert.Null(exception);
            Assert.True(spell.IsFinishing);
            Assert.Equal(readsAfterFailure, context.CasterVitalReads);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
        }

        [Fact]
        public void PersistenceFalseBeforeDueTick_SkipsActivationAndFinishes()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Vital, PrerequisiteComparison.GreaterThan, 25u, (uint)Vital.Resource0));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterResource0 = 50f
            };
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                tickTime: 250u,
                cost: 25u,
                costVital: Vital.Resource0,
                flags: SpellEffectFlags.CancelOnly);
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                effect,
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);
            spell.Cast();
            spell.Update(0d);
            context.CasterResource0 = 25f;

            spell.Update(0.25d);

            Assert.True(spell.IsFinishing);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<Server07F8>());
        }

        [Fact]
        public void SprintShapedTick_InvalidatesAfterOneActivationThenCleansAndFinishesOnce()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                38044u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Vital, PrerequisiteComparison.GreaterThan, 25u, (uint)Vital.Resource0));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterResource0 = 50f
            };
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                tickTime: 250u,
                cost: 25u,
                costVital: Vital.Resource0,
                flags: SpellEffectFlags.CancelOnly);
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                effect,
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);
            var operations = new List<string>();
            Mock<IUnitEntity> modifierTarget = context.CreateTarget(99u);
            modifierTarget.Setup(entity => entity.AddSpellModifierProperty(It.IsAny<ISpellPropertyModifier>()))
                .Returns(true);
            modifierTarget.Setup(entity => entity.RemoveSpellModifierProperty(
                    Property.Strength,
                    It.IsAny<SpellEffectIdentity>()))
                .Callback(() => operations.Add("cleanup"))
                .Returns(true);
            context.PacketObserved = packet =>
            {
                if (packet is ServerSpellFinish)
                    operations.Add("finish");
            };
            spell.Cast();
            spell.Update(0d);
            var modifier = new SpellPropertyModifier(
                new SpellEffectIdentity(spell.CastingId, 123u, 999u),
                Property.Strength,
                1u,
                0f,
                5f,
                0f);
            Assert.True(spell.ApplyPropertyModifier(modifierTarget.Object, modifier));

            spell.Update(0.25d);

            Assert.Equal(25f, context.CasterResource0);
            Assert.Equal([-25f], context.CostMutations);
            Assert.Single(context.Invocations);
            Assert.Single(context.Packets.OfType<Server07F8>());
            Assert.True(spell.IsFinishing);
            Assert.Equal(["cleanup"], operations);
            Assert.Empty(context.Packets.OfType<ServerSpellFinish>());

            spell.LateUpdate(0d);
            spell.LateUpdate(0d);
            spell.Update(0d);
            spell.Dispose();

            Assert.True(spell.IsFinished);
            Assert.Equal(["cleanup", "finish"], operations);
            ServerSpellFinish finish = Assert.Single(context.Packets.OfType<ServerSpellFinish>());
            Assert.Equal(spell.CastingId, finish.ServerUniqueId);
            modifierTarget.Verify(entity => entity.RemoveSpellModifierProperty(
                Property.Strength,
                modifier.Identity), Times.Once);
        }

        [Theory]
        [InlineData(PersistenceTargetDrift.Predicate)]
        [InlineData(PersistenceTargetDrift.Dead)]
        [InlineData(PersistenceTargetDrift.OutOfWorld)]
        [InlineData(PersistenceTargetDrift.CrossMap)]
        [InlineData(PersistenceTargetDrift.Rebound)]
        [InlineData(PersistenceTargetDrift.Disappeared)]
        [InlineData(PersistenceTargetDrift.LookupThrows)]
        public void SupportedTargetPersistence_CapturesExactTargetAndFailsOnDrift(
            PersistenceTargetDrift drift)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThanOrEqual, 10u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            uint originalLevel = 10u;
            bool alive = true;
            bool inWorld = true;
            IBaseMap targetMap = context.Map.Object;
            Mock<IUnitEntity> original = context.CreateTarget(
                42u,
                level: () => originalLevel,
                alive: () => alive,
                inWorld: () => inWorld,
                map: () => targetMap);
            Mock<IUnitEntity> replacement = context.CreateTarget(42u, level: () => 10u);
            IWorldEntity visible = original.Object;
            bool throwOnLookup = false;
            context.Caster.Setup(entity => entity.GetVisible<IWorldEntity>(42u))
                .Returns(() => throwOnLookup
                    ? throw new InvalidOperationException("Test persistence target lookup failure.")
                    : visible);
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u),
                targetPersistencePrerequisite: prerequisite.Id,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, original.Object)]);
            spell.Cast();

            switch (drift)
            {
                case PersistenceTargetDrift.Predicate:
                    originalLevel = 9u;
                    break;
                case PersistenceTargetDrift.Dead:
                    alive = false;
                    break;
                case PersistenceTargetDrift.OutOfWorld:
                    inWorld = false;
                    break;
                case PersistenceTargetDrift.CrossMap:
                    targetMap = Mock.Of<IBaseMap>();
                    break;
                case PersistenceTargetDrift.Rebound:
                    visible = replacement.Object;
                    break;
                case PersistenceTargetDrift.Disappeared:
                    visible = null;
                    break;
                case PersistenceTargetDrift.LookupThrows:
                    throwOnLookup = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(drift));
            }

            spell.Update(0d);

            Assert.True(spell.IsFinishing);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
        }

        public enum PersistenceTargetDrift
        {
            Predicate,
            Dead,
            OutOfWorld,
            CrossMap,
            Rebound,
            Disappeared,
            LookupThrows
        }

        private static Spell4EffectsEntry CreateEffect(
            uint id,
            uint casterPrerequisite = 0u,
            uint targetPrerequisite = 0u,
            uint delayTime = 0u,
            uint tickTime = 0u,
            uint durationTime = 0u,
            uint cost = 0u,
            Vital costVital = Vital.Resource1,
            SpellEffectFlags flags = SpellEffectFlags.None)
        {
            return new Spell4EffectsEntry
            {
                Id                        = id,
                EffectType                = SpellEffectType.UNUSED039,
                TargetFlags               = (uint)SpellEffectTargetFlags.Target,
                DelayTime                 = delayTime,
                TickTime                  = tickTime,
                DurationTime              = durationTime,
                Flags                     = (uint)flags,
                PhaseFlags                = 1u,
                PrerequisiteIdCasterApply = casterPrerequisite,
                PrerequisiteIdTargetApply = targetPrerequisite,
                InnateCostPerTickType0     = (uint)costVital,
                InnateCostPerTick0         = cost,
                ParameterType             = new SpellEffectParameterType[4],
                ParameterValue            = new float[4]
            };
        }

        private static PrerequisiteEntry CreateEntry(
            uint id,
            EvaluationMode mode,
            params (PrerequisiteType Type, PrerequisiteComparison Comparison, uint Value, uint ObjectId)[] components)
        {
            var entry = new PrerequisiteEntry
            {
                Id                       = id,
                Flags                    = mode,
                PrerequisiteTypeId       = new PrerequisiteType[3],
                PrerequisiteComparisonId = new PrerequisiteComparison[3],
                Value                    = new uint[3],
                ObjectId                 = new uint[3]
            };

            for (int i = 0; i < components.Length; i++)
            {
                entry.PrerequisiteTypeId[i] = components[i].Type;
                entry.PrerequisiteComparisonId[i] = components[i].Comparison;
                entry.Value[i] = components[i].Value;
                entry.ObjectId[i] = components[i].ObjectId;
            }

            return entry;
        }

        private static GameTable<PrerequisiteEntry> CreateGameTable(params PrerequisiteEntry[] entries)
        {
            var table = (GameTable<PrerequisiteEntry>)RuntimeHelpers.GetUninitializedObject(
                typeof(GameTable<PrerequisiteEntry>));
            typeof(GameTable<PrerequisiteEntry>)
                .GetProperty(nameof(GameTable<PrerequisiteEntry>.Entries))
                ?.SetValue(table, entries);

            int lookupLength = checked((int)entries.Max(entry => entry.Id) + 1);
            int[] lookup = Enumerable.Repeat(-1, lookupLength).ToArray();
            for (int i = 0; i < entries.Length; i++)
                lookup[entries[i].Id] = i;

            typeof(GameTable<PrerequisiteEntry>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<PrerequisiteEntry>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = (ulong)lookupLength });
            return table;
        }

        private static GameTable<Faction2Entry> CreateFactionTable()
        {
            Faction2Entry[] entries =
            [
                new Faction2Entry { Id = 166u },
                new Faction2Entry { Id = 167u },
                new Faction2Entry { Id = 170u },
                new Faction2Entry { Id = 171u },
                new Faction2Entry { Id = 390u },
                new Faction2Entry { Id = 391u },
                new Faction2Entry { Id = 392u }
            ];
            var table = (GameTable<Faction2Entry>)RuntimeHelpers.GetUninitializedObject(
                typeof(GameTable<Faction2Entry>));
            typeof(GameTable<Faction2Entry>)
                .GetProperty(nameof(GameTable<Faction2Entry>.Entries))
                ?.SetValue(table, entries);

            int[] lookup = Enumerable.Repeat(-1, 393).ToArray();
            for (int i = 0; i < entries.Length; i++)
                lookup[entries[i].Id] = i;

            typeof(GameTable<Faction2Entry>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<Faction2Entry>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = (ulong)lookup.Length });
            return table;
        }

        private sealed class SpellPrerequisiteContext : IDisposable
        {
            public Mock<IPlayer> Caster { get; } = new();
            public Mock<IBaseMap> Map { get; } = new();
            public Mock<IGameSession> Session { get; } = new();
            public List<IWritable> Packets { get; } = [];
            public List<(ISpell Spell, IUnitEntity Target, ISpellTargetEffectInfo Effect)> Invocations { get; } = [];
            public List<float> CostMutations { get; } = [];
            public List<IWritable> SessionPackets { get; } = [];
            public int InvocationAttempts { get; private set; }
            public uint CasterLevel { get; set; } = 50u;
            public int CasterLevelReads { get; private set; }
            public int CasterVitalReads { get; private set; }
            public Faction CasterFaction { get; set; } = Faction.Exile;
            public float CasterResource0 { get; set; } = 100f;
            public float CasterResource1 { get; set; } = 100f;
            public bool ThrowOnCasterVitalRead { get; set; }
            public Action<ISpell, IUnitEntity, ISpellTargetEffectInfo> Handler { get; set; }
            public Action<IWritable> PacketObserved { get; set; }

            private readonly IServiceProvider previousProvider;
            private readonly ServiceProvider serviceProvider;

            public SpellPrerequisiteContext(params PrerequisiteEntry[] prerequisites)
            {
                previousProvider = LegacyServiceProvider.Provider;

                var scriptCollection = new Mock<IScriptCollection>();
                var scriptManager = new Mock<IScriptManager>();
                scriptManager.Setup(manager => manager.InitialiseOwnedScripts<ISpell>(
                        It.IsAny<ISpell>(), It.IsAny<uint>()))
                    .Returns(scriptCollection.Object);

                var globalSpellManager = new GlobalSpellManager();
                FieldInfo handlerField = typeof(GlobalSpellManager).GetField(
                    "spellEffectDelegates",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var handlers = (Dictionary<SpellEffectType, SpellEffectDelegate>)handlerField.GetValue(globalSpellManager);
                handlers.Add(SpellEffectType.UNUSED039, (spell, target, effect) =>
                {
                    InvocationAttempts++;
                    Handler?.Invoke(spell, target, effect);
                    Invocations.Add((spell, target, effect));
                });

                var gameTableManager = new Mock<IGameTableManager>();
                gameTableManager.SetupGet(manager => manager.Prerequisite)
                    .Returns(CreateGameTable(prerequisites));
                gameTableManager.SetupGet(manager => manager.Faction2)
                    .Returns(CreateFactionTable());
                var parametersFactory = new Mock<IFactory<IPrerequisiteParameters>>();

                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton(scriptManager.Object);
                services.AddSingleton(globalSpellManager);
                services.AddSingleton<IGameTableManager>(gameTableManager.Object);
                services.AddSingleton(parametersFactory.Object);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckLevel>(PrerequisiteType.Level);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckVital>(PrerequisiteType.Vital);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckBaseFaction>(PrerequisiteType.BaseFaction);
                services.AddSingleton<PrerequisiteManager>();
                services.AddSingleton<IPrerequisiteManager>(provider => provider.GetRequiredService<PrerequisiteManager>());
                serviceProvider = services.BuildServiceProvider();
                LegacyServiceProvider.Provider = serviceProvider;

                var spellManager = new Mock<ISpellManager>();
                Caster.SetupGet(entity => entity.Guid).Returns(7u);
                Caster.SetupGet(entity => entity.IsAlive).Returns(true);
                Caster.SetupGet(entity => entity.InWorld).Returns(true);
                Caster.SetupGet(entity => entity.Map).Returns(Map.Object);
                Caster.SetupGet(entity => entity.IsLoading).Returns(false);
                Caster.SetupGet(entity => entity.Session).Returns(Session.Object);
                Caster.SetupGet(entity => entity.SpellManager).Returns(spellManager.Object);
                Caster.SetupGet(entity => entity.Level).Returns(() =>
                {
                    CasterLevelReads++;
                    return CasterLevel;
                });
                Caster.SetupGet(entity => entity.Faction1).Returns(() => CasterFaction);
                Caster.Setup(entity => entity.TryGetVitalValue(
                        It.IsAny<Vital>(),
                        out It.Ref<float>.IsAny))
                    .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                    {
                        CasterVitalReads++;
                        if (ThrowOnCasterVitalRead)
                            throw new InvalidOperationException("Test caster vital read failure.");

                        switch (vital)
                        {
                            case Vital.Resource0:
                                value = CasterResource0;
                                return true;
                            case Vital.Resource1:
                                value = CasterResource1;
                                return true;
                            default:
                                value = 0f;
                                return false;
                        }
                    }));
                Caster.Setup(entity => entity.TryModifyVital(
                        It.IsAny<Vital>(),
                        It.IsAny<float>(),
                        It.IsAny<IUnitEntity>()))
                    .Returns((Vital vital, float delta, IUnitEntity _) =>
                    {
                        switch (vital)
                        {
                            case Vital.Resource0:
                                CasterResource0 += delta;
                                break;
                            case Vital.Resource1:
                                CasterResource1 += delta;
                                break;
                            default:
                                return false;
                        }

                        CostMutations.Add(delta);
                        return true;
                    });
                Caster.Setup(entity => entity.EnqueueToVisible(
                        It.IsAny<IWritable>(),
                        It.IsAny<bool>()))
                    .Callback<IWritable, bool>((packet, _) =>
                    {
                        Packets.Add(packet);
                        PacketObserved?.Invoke(packet);
                    });
                Session.Setup(session => session.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                    .Callback<IWritable>(SessionPackets.Add);
            }

            public Mock<IUnitEntity> CreateTarget(
                uint guid,
                Func<uint> level = null,
                Func<Faction> faction = null,
                Func<Vital, float?> vital = null,
                Func<bool> alive = null,
                Func<bool> inWorld = null,
                Func<IBaseMap> map = null)
            {
                var target = new Mock<IUnitEntity>();
                target.SetupGet(entity => entity.Guid).Returns(guid);
                target.SetupGet(entity => entity.IsAlive).Returns(() => alive?.Invoke() ?? true);
                target.SetupGet(entity => entity.InWorld).Returns(() => inWorld?.Invoke() ?? true);
                target.SetupGet(entity => entity.Map).Returns(() => map?.Invoke() ?? Map.Object);
                target.SetupGet(entity => entity.Level).Returns(() => level?.Invoke() ?? 1u);
                target.SetupGet(entity => entity.Faction1).Returns(() => faction?.Invoke() ?? Faction.Dominion);
                target.Setup(entity => entity.TryGetVitalValue(
                        It.IsAny<Vital>(),
                        out It.Ref<float>.IsAny))
                    .Returns(new TryGetVitalValue((Vital vitalId, out float value) =>
                    {
                        float? current = vital?.Invoke(vitalId);
                        value = current ?? 0f;
                        return current.HasValue;
                    }));
                target.Setup(entity => entity.EnqueueToVisible(
                        It.IsAny<IWritable>(),
                        It.IsAny<bool>()))
                    .Callback<IWritable, bool>((packet, _) => Packets.Add(packet));
                return target;
            }

            public TestApplyPrerequisiteSpell CreateSpell(
                Spell4EffectsEntry effect,
                params (SpellEffectTargetFlags Flags, IUnitEntity Entity)[] targets)
            {
                var spell = new TestApplyPrerequisiteSpell(
                    Caster.Object,
                    CreateParameters(CastMethod.Normal, effect),
                    () => targets);
                spell.SetStatus(SpellStatus.Casting);
                return spell;
            }

            public TestApplyPrerequisiteSpell CreatePersistenceSpell(
                Spell4EffectsEntry effect,
                uint casterPersistencePrerequisite = 0u,
                uint targetPersistencePrerequisite = 0u,
                uint primaryTargetId = 0u,
                params (SpellEffectTargetFlags Flags, IUnitEntity Entity)[] targets)
            {
                SpellParameters parameters = CreateParameters(CastMethod.Normal, effect);
                parameters.SpellInfo.Entry.PrerequisiteIdCasterPersistence = casterPersistencePrerequisite;
                parameters.SpellInfo.Entry.PrerequisiteIdTargetPersistence = targetPersistencePrerequisite;
                parameters.PrimaryTargetId = primaryTargetId;
                return new TestApplyPrerequisiteSpell(
                    Caster.Object,
                    parameters,
                    () => targets);
            }

            public TestApplyPrerequisiteAura CreateAura(
                Spell4EffectsEntry effect,
                IUnitEntity target)
            {
                Caster.Setup(entity => entity.GetVisible<IUnitEntity>(target.Guid))
                    .Returns(target);
                SpellParameters parameters = CreateParameters(CastMethod.Aura, effect);
                parameters.PrimaryTargetId = target.Guid;
                var spell = new TestApplyPrerequisiteAura(Caster.Object, parameters);
                spell.SetStatus(SpellStatus.Casting);
                return spell;
            }

            public void Dispose()
            {
                LegacyServiceProvider.Provider = previousProvider;
                serviceProvider.Dispose();
            }

            private static SpellParameters CreateParameters(
                CastMethod castMethod,
                Spell4EffectsEntry effect)
            {
                var baseInfo = new Mock<ISpellBaseInfo>();
                baseInfo.SetupGet(info => info.Entry).Returns(new Spell4BaseEntry
                {
                    CastMethod = (uint)castMethod
                });
                var spellInfo = new Mock<ISpellInfo>();
                spellInfo.SetupGet(info => info.Entry).Returns(new Spell4Entry
                {
                    Id            = 123u,
                    SpellDuration = castMethod == CastMethod.Aura ? 1_000u : 0u
                });
                spellInfo.SetupGet(info => info.BaseInfo).Returns(baseInfo.Object);
                spellInfo.SetupGet(info => info.Effects).Returns([effect]);
                spellInfo.SetupGet(info => info.Telegraphs).Returns([]);
                spellInfo.SetupGet(info => info.PrerequisiteRunners).Returns([]);
                return new SpellParameters
                {
                    SpellInfo = spellInfo.Object
                };
            }
        }

        private sealed class ThrowingPrerequisiteServiceProvider(IServiceProvider inner) : IServiceProvider
        {
            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(PrerequisiteManager))
                    throw new InvalidOperationException("Test persistence classification failure.");

                return inner.GetService(serviceType);
            }
        }

        private sealed class TestApplyPrerequisiteSpell : NexusForever.Game.Spell.Spell
        {
            private readonly Func<IReadOnlyList<(SpellEffectTargetFlags Flags, IUnitEntity Entity)>> targetProvider;

            public TestApplyPrerequisiteSpell(
                IUnitEntity caster,
                ISpellParameters parameters,
                Func<IReadOnlyList<(SpellEffectTargetFlags Flags, IUnitEntity Entity)>> targetProvider)
                : base(caster, parameters)
            {
                this.targetProvider = targetProvider;
            }

            public void ExecuteForTest()
            {
                Execute();
            }

            public void SetStatus(SpellStatus value)
            {
                status = value;
            }

            protected override void SelectTargets()
            {
                foreach ((SpellEffectTargetFlags flags, IUnitEntity entity) in targetProvider())
                    targets.Add(new SpellTargetInfo(flags, entity));
            }
        }

        private sealed class TestApplyPrerequisiteAura : SpellAura
        {
            public TestApplyPrerequisiteAura(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            public void ExecuteForTest()
            {
                Execute();
            }

            public void SetStatus(SpellStatus value)
            {
                status = value;
            }
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
    }
}
