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
using NexusForever.Game.Static.Setting;
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
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LegacyCasterCast_InCombatUsesPlayerPrerequisiteCheck(bool inCombat)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterInCombat = inCombat
            };
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                casterCastPrerequisite: prerequisite,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            spell.Update(0d);

            if (inCombat)
            {
                Assert.Empty(context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Single(context.Invocations);
                Assert.Single(context.Packets.OfType<ServerSpellGo>());
            }
            else
            {
                ServerSpellCastResult result = Assert.Single(
                    context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Equal(CastResult.PrereqCasterCast, result.CastResult);
                Assert.Empty(context.Invocations);
                Assert.Empty(context.Packets.OfType<ServerSpellStart>());
                Assert.Empty(context.Packets.OfType<ServerSpellGo>());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LegacyCasterCast_DeadStateUsesPlayerPrerequisiteCheck(bool isAlive)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterAlive = isAlive
            };
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                casterCastPrerequisite: prerequisite,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            spell.Update(0d);

            if (isAlive)
            {
                Assert.Empty(context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Single(context.Invocations);
                Assert.Single(context.Packets.OfType<ServerSpellGo>());
            }
            else
            {
                ServerSpellCastResult result = Assert.Single(
                    context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Equal(CastResult.PrereqCasterCast, result.CastResult);
                Assert.Empty(context.Invocations);
                Assert.Empty(context.Packets.OfType<ServerSpellStart>());
                Assert.Empty(context.Packets.OfType<ServerSpellGo>());
            }
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, true)]
        [InlineData(PrerequisiteComparison.NotEqual, false)]
        public void LegacyCasterCast_IsPlayerUsesSuppliedPlayer(
            PrerequisiteComparison comparison,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, comparison, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                casterCastPrerequisite: prerequisite,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            spell.Update(0d);

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            if (!expectedInvocation)
            {
                ServerSpellCastResult result = Assert.Single(
                    context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Equal(CastResult.PrereqCasterCast, result.CastResult);
            }
        }

        [Theory]
        [InlineData(49u, true)]
        [InlineData(50u, false)]
        public void LegacyCasterCast_HealthPercentageUsesCurrentPlayerState(
            uint currentHealth,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                315u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.LessThan, 50u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterHealth = currentHealth,
                CasterMaxHealth = 100u
            };
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                casterCastPrerequisite: prerequisite,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            if (expectedInvocation)
                spell.Update(0d);

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            if (!expectedInvocation)
            {
                ServerSpellCastResult result = Assert.Single(
                    context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Equal(CastResult.PrereqCasterCast, result.CastResult);
                Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            }
        }

        [Theory]
        [InlineData(WorldDifficulty.Normal, false)]
        [InlineData(WorldDifficulty.Veteran, true)]
        public void LegacyCasterCast_DifficultyBuildRowUsesCasterMapAuthority(
            WorldDifficulty difficulty,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                14893u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Veteran, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            context.Map.SetupGet(value => value.Difficulty).Returns(difficulty);
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                casterCastPrerequisite: prerequisite,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            if (expectedInvocation)
                spell.Update(0d);

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            if (!expectedInvocation)
            {
                ServerSpellCastResult result = Assert.Single(
                    context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Equal(CastResult.PrereqCasterCast, result.CastResult);
                Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            }
        }

        [Theory]
        [InlineData(0u, true)]
        [InlineData(31_965u, false)]
        public void LegacyCasterCast_IsCreatureBuildRowUsesCasterCreatureId(
            uint creatureId,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                11630u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.NotEqual, 31_965u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterCreatureId = creatureId
            };
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                casterCastPrerequisite: prerequisite,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            if (expectedInvocation)
                spell.Update(0d);

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            Assert.Equal(
                expectedInvocation ? 0 : 1,
                context.SessionPackets.OfType<ServerSpellCastResult>().Count());
            if (!expectedInvocation)
            {
                ServerSpellCastResult result = Assert.Single(
                    context.SessionPackets.OfType<ServerSpellCastResult>());
                Assert.Equal(CastResult.PrereqCasterCast, result.CastResult);
                Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            }

            context.Caster.VerifyGet(unit => unit.CreatureId, Times.Once);
        }

        [Fact]
        public void CasterRunner_IsPlayerCanOverrideCasterCastPrerequisite()
        {
            PrerequisiteEntry casterPrerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.NotEqual, 0u, 0u));
            PrerequisiteEntry runnerPrerequisite = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(
                casterPrerequisite,
                runnerPrerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                casterCastPrerequisite: casterPrerequisite,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);
            spell.Parameters.SpellInfo.PrerequisiteRunners.Add(runnerPrerequisite);

            spell.Cast();
            spell.Update(0d);

            Assert.Empty(context.SessionPackets.OfType<ServerSpellCastResult>());
            Assert.Single(context.Invocations);
            Assert.Single(context.Packets.OfType<ServerSpellGo>());
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void TargetCast_IsPlayerUsesExactTargetForPlayerAndNpcCasters(
            bool playerCaster,
            bool playerTarget,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            IUnitEntity target = playerTarget
                ? context.CreatePlayerTarget(42u).Object
                : context.CreateTarget(42u).Object;
            IUnitEntity caster;
            if (playerCaster)
            {
                caster = context.Caster.Object;
                context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                    .Returns(target);
            }
            else
            {
                Mock<IUnitEntity> npc = context.CreateTarget(50u);
                npc.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                    .Returns(target);
                caster = npc.Object;
            }

            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                caster: caster,
                targets: [(SpellEffectTargetFlags.Target, target)]);

            spell.Cast();
            if (expectedInvocation)
                spell.Update(0d);

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            if (!expectedInvocation)
                Assert.True(spell.IsFinishing);
            Assert.Equal(
                playerCaster && !expectedInvocation ? 1 : 0,
                context.SessionPackets.OfType<ServerSpellCastResult>().Count());
        }

        [Fact]
        public void TargetCast_LevelUsesExactVisibleTarget()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 50u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterLevel = 50u
            };
            Mock<IUnitEntity> target = context.CreateTarget(42u, level: () => 51u);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            spell.Update(0d);

            Assert.False(spell.IsFinishing);
            Assert.Single(context.Invocations);
            Assert.Single(context.Packets.OfType<ServerSpellGo>());
            Assert.Equal(0, context.CasterLevelReads);
            target.VerifyGet(unit => unit.Level, Times.Once);
        }

        [Theory]
        [InlineData(28_559u, false)]
        [InlineData(28_560u, true)]
        public void TargetCast_IsCreatureBuildRowUsesExactVisibleTargetBeforeCost(
            uint creatureId,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                13215u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsCreature, PrerequisiteComparison.NotEqual, 28_559u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                creatureId: () => creatureId);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, cost: 10u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            if (expectedInvocation)
                spell.Update(0d);

            Assert.Equal(expectedInvocation ? 90f : 100f, context.CasterResource1);
            Assert.Equal(expectedInvocation ? 1 : 0, context.CostMutations.Count);
            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            if (!expectedInvocation)
                AssertTargetCastFailure(context, spell);
            target.VerifyGet(unit => unit.CreatureId, Times.Once);
        }

        [Theory]
        [InlineData(true, 2u, true)]
        [InlineData(true, 1u, false)]
        [InlineData(false, 2u, true)]
        [InlineData(false, 1u, false)]
        public void TargetCast_HealthRequirementUsesExactVisibleTargetForPlayerAndNpcCasters(
            bool playerCaster,
            uint targetHealth,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                7958u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThan, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                health: () => targetHealth);
            IUnitEntity caster;
            if (playerCaster)
            {
                context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                    .Returns(target.Object);
                caster = context.Caster.Object;
            }
            else
            {
                Mock<IUnitEntity> npc = context.CreateTarget(50u);
                npc.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                    .Returns(target.Object);
                caster = npc.Object;
            }

            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, cost: 10u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                caster: caster,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            if (expectedInvocation)
                spell.Update(0d);

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            Assert.Equal(expectedInvocation ? 1 : 0, context.Packets.OfType<ServerSpellGo>().Count());
            Assert.Equal(
                playerCaster && !expectedInvocation ? 1 : 0,
                context.SessionPackets.OfType<ServerSpellCastResult>().Count());
            if (!expectedInvocation)
            {
                Assert.True(spell.IsFinishing);
                Assert.Empty(context.CostMutations);
                Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            }

            target.VerifyGet(unit => unit.Health, Times.Once);
            target.VerifyGet(unit => unit.MaxHealth, Times.Never);
        }

        [Fact]
        public void TargetCast_HealthRequirementReadFailureFailsBeforeStart()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                7958u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThan, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                health: () => throw new InvalidOperationException("Test target health read failure."));
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, cost: 10u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            Exception exception = Record.Exception(spell.Cast);

            Assert.Null(exception);
            AssertTargetCastFailure(context, spell);
            target.VerifyGet(unit => unit.Health, Times.Once);
        }

        [Fact]
        public void TargetCast_HealthRequirementIsAdmissionOnlyNotPersistence()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                7958u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThan, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            uint health = 2u;
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                health: () => health);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            health = 0u;
            spell.Update(0d);

            Assert.False(spell.IsFinishing);
            Assert.Single(context.Invocations);
            Assert.Single(context.Packets.OfType<ServerSpellGo>());
            target.VerifyGet(unit => unit.Health, Times.Once);
        }

        [Theory]
        [InlineData(true, 50u, true)]
        [InlineData(true, 49u, false)]
        [InlineData(false, 50u, true)]
        [InlineData(false, 49u, false)]
        public void TargetCast_HealthPercentageUsesExactVisibleTargetForPlayerAndNpcCasters(
            bool playerCaster,
            uint targetHealth,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.GreaterThanOrEqual, 50u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                health: () => targetHealth,
                maximumHealth: () => 100u);
            IUnitEntity caster;
            if (playerCaster)
            {
                context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                    .Returns(target.Object);
                caster = context.Caster.Object;
            }
            else
            {
                Mock<IUnitEntity> npc = context.CreateTarget(50u);
                npc.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                    .Returns(target.Object);
                caster = npc.Object;
            }

            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, cost: 10u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                caster: caster,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            if (expectedInvocation)
                spell.Update(0d);

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            Assert.Equal(expectedInvocation ? 1 : 0, context.Packets.OfType<ServerSpellGo>().Count());
            if (!expectedInvocation)
            {
                Assert.True(spell.IsFinishing);
                Assert.Empty(context.CostMutations);
                Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            }

            target.VerifyGet(unit => unit.Health, Times.Once);
            target.VerifyGet(unit => unit.MaxHealth, Times.Once);
        }

        [Fact]
        public void TargetCast_HealthPercentageReadFailureFailsBeforeStart()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.GreaterThanOrEqual, 50u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                health: () => 50u,
                maximumHealth: () => throw new InvalidOperationException(
                    "Test target max-health read failure."));
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, cost: 10u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            Exception exception = Record.Exception(spell.Cast);

            Assert.Null(exception);
            AssertTargetCastFailure(context, spell);
            target.VerifyGet(unit => unit.Health, Times.Once);
            target.VerifyGet(unit => unit.MaxHealth, Times.Once);
        }

        [Fact]
        public void TargetCast_UnmetVisibleTargetFailsBeforeStart()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterInCombat = true
            };
            Mock<IUnitEntity> target = context.CreateTarget(42u, inCombat: () => false);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, cost: 10u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();

            AssertTargetCastFailure(context, spell);
            context.Caster.VerifyGet(unit => unit.InCombat, Times.Never);
            target.VerifyGet(unit => unit.InCombat, Times.Once);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(7u)]
        public void TargetCast_ZeroOrExactCasterGuidUsesCaster(uint primaryTargetId)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 50u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterLevel = 51u
            };
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(7u))
                .Returns((IWorldEntity)null);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: primaryTargetId,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);

            spell.Cast();
            spell.Update(0d);

            Assert.False(spell.IsFinishing);
            Assert.Single(context.Invocations);
            Assert.Equal(1, context.CasterLevelReads);
        }

        [Fact]
        public void TargetCast_NonPlayerCasterUsesUnitEvaluation()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> npc = context.CreateTarget(42u, inCombat: () => true);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: prerequisite,
                caster: npc.Object,
                targets: [(SpellEffectTargetFlags.Target, npc.Object)]);

            spell.Cast();
            spell.Update(0d);

            Assert.False(spell.IsFinishing);
            Assert.Single(context.Invocations);
            Assert.Empty(context.SessionPackets);
            npc.VerifyGet(unit => unit.InCombat, Times.Once);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void TargetCast_DeadStateUsesExactVisibleTarget(
            bool targetIsAlive,
            bool expectedFailure)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                alive: () => targetIsAlive);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            spell.Update(0d);

            if (expectedFailure)
            {
                AssertTargetCastFailure(context, spell);
            }
            else
            {
                Assert.False(spell.IsFinishing);
                Assert.Single(context.Invocations);
                Assert.Single(context.Packets.OfType<ServerSpellGo>());
            }

            target.VerifyGet(unit => unit.IsAlive, Times.AtLeastOnce);
        }

        [Fact]
        public void TargetCast_DeadStateSupportsNonPlayerCaster()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(42u, alive: () => false);
            Mock<IUnitEntity> npc = context.CreateTarget(50u);
            npc.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                caster: npc.Object,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            spell.Update(0d);

            Assert.False(spell.IsFinishing);
            Assert.Single(context.Invocations);
            Assert.Empty(context.SessionPackets);
            target.VerifyGet(unit => unit.IsAlive, Times.AtLeastOnce);
        }

        [Theory]
        [InlineData(TargetCastUnsupportedShape.Missing)]
        [InlineData(TargetCastUnsupportedShape.Mixed)]
        [InlineData(TargetCastUnsupportedShape.Malformed)]
        public void TargetCast_UnsupportedWholeRowPreservesLegacyWithoutStateReads(
            TargetCastUnsupportedShape shape)
        {
            PrerequisiteEntry prerequisite = shape switch
            {
                TargetCastUnsupportedShape.Missing => CreateEntry(
                    1u,
                    EvaluationMode.EvaluateAND,
                    (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u)),
                TargetCastUnsupportedShape.Mixed => CreateEntry(
                    2u,
                    EvaluationMode.EvaluateAND,
                    (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 50u, 0u),
                    (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u)),
                TargetCastUnsupportedShape.Malformed => CreateEntry(
                    3u,
                    EvaluationMode.EvaluateAND,
                    (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 50u, 0u),
                    (PrerequisiteType.None, PrerequisiteComparison.Equal, 1u, 0u)),
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterLevel = 1u
            };
            uint prerequisiteId = shape == TargetCastUnsupportedShape.Missing
                ? 99u
                : prerequisite.Id;
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: shape == TargetCastUnsupportedShape.Missing
                    ? null
                    : prerequisite,
                targetCastPrerequisiteId: prerequisiteId,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);

            spell.Cast();
            spell.Update(0d);

            Assert.False(spell.IsFinishing);
            Assert.Single(context.Invocations);
            Assert.Equal(0, context.CasterLevelReads);
            context.Caster.VerifyGet(unit => unit.InCombat, Times.Never);
        }

        [Theory]
        [InlineData(TargetCastResolutionFailure.Missing)]
        [InlineData(TargetCastResolutionFailure.NonUnit)]
        [InlineData(TargetCastResolutionFailure.LookupThrows)]
        public void TargetCast_SupportedTargetResolutionFailureFailsClosed(
            TargetCastResolutionFailure failure)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 50u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            IWorldEntity nonUnit = Mock.Of<IWorldEntity>();
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(() => failure switch
                {
                    TargetCastResolutionFailure.Missing => null,
                    TargetCastResolutionFailure.NonUnit => nonUnit,
                    TargetCastResolutionFailure.LookupThrows => throw new InvalidOperationException(
                        "Test target cast lookup failure."),
                    _ => throw new ArgumentOutOfRangeException(nameof(failure))
                });
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, cost: 10u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u);

            Exception exception = Record.Exception(spell.Cast);

            Assert.Null(exception);
            AssertTargetCastFailure(context, spell);
            context.Caster.Verify(
                unit => unit.GetVisible<IWorldEntity>(42u),
                Times.Once);
        }

        [Fact]
        public void TargetCast_SupportedEvaluationFailureFailsClosed()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 50u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                level: () => throw new InvalidOperationException(
                    "Test target cast prerequisite read failure."));
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, cost: 10u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u);

            Exception exception = Record.Exception(spell.Cast);

            Assert.Null(exception);
            AssertTargetCastFailure(context, spell);
            target.VerifyGet(unit => unit.Level, Times.Once);
        }

        [Fact]
        public void TargetCast_IsEvaluatedOnlyAtCastTime()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            bool targetInCombat = true;
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                inCombat: () => targetInCombat);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u, delayTime: 100u),
                targetCastPrerequisite: prerequisite,
                primaryTargetId: 42u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);

            spell.Cast();
            spell.Update(0d);
            targetInCombat = false;
            spell.Update(0.1d);

            Assert.False(spell.IsFinishing);
            Assert.Single(context.Invocations);
            target.VerifyGet(unit => unit.InCombat, Times.Once);
        }

        [Fact]
        public void TargetCast_FailurePrecedesPersistenceEvaluation()
        {
            PrerequisiteEntry targetPrerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry persistencePrerequisite = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 50u, 0u));
            using var context = new SpellPrerequisiteContext(
                targetPrerequisite,
                persistencePrerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(42u, inCombat: () => false);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                targetCastPrerequisite: targetPrerequisite,
                primaryTargetId: 42u);
            spell.Parameters.SpellInfo.Entry.PrerequisiteIdCasterPersistence = persistencePrerequisite.Id;

            spell.Cast();

            AssertTargetCastFailure(context, spell);
            Assert.Equal(0, context.CasterLevelReads);
        }

        [Fact]
        public void TargetCast_IsNotOverriddenByCasterRunner()
        {
            PrerequisiteEntry casterPrerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry runnerPrerequisite = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Level, PrerequisiteComparison.GreaterThan, 1u, 0u));
            PrerequisiteEntry targetPrerequisite = CreateEntry(
                3u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(
                casterPrerequisite,
                runnerPrerequisite,
                targetPrerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(42u, inCombat: () => false);
            context.Caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u))
                .Returns(target.Object);
            TestApplyPrerequisiteSpell spell = context.CreateCastSpell(
                CreateEffect(1u),
                casterCastPrerequisite: casterPrerequisite,
                targetCastPrerequisite: targetPrerequisite,
                primaryTargetId: 42u);
            spell.Parameters.SpellInfo.PrerequisiteRunners.Add(runnerPrerequisite);

            spell.Cast();

            AssertTargetCastFailure(context, spell);
            target.VerifyGet(unit => unit.InCombat, Times.Once);
        }

        [Theory]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        public void UnitApply_InCombatChecksCasterAndTarget(
            bool casterInCombat,
            bool targetInCombat,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterInCombat = casterInCombat
            };
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                inCombat: () => targetInCombat);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                casterPrerequisite: prerequisite.Id,
                targetPrerequisite: prerequisite.Id);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            Assert.Equal(
                expectedInvocation ? 1 : 0,
                Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData.Count);
            Assert.Empty(context.Packets.OfType<Server07F8>());
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        public void UnitApply_DeadStateChecksLivingCasterAndDeadTarget(
            bool casterIsAlive,
            bool targetIsAlive,
            bool expectedInvocation)
        {
            PrerequisiteEntry livingPrerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 0u, 0u));
            PrerequisiteEntry deadPrerequisite = CreateEntry(
                2u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new SpellPrerequisiteContext(
                livingPrerequisite,
                deadPrerequisite)
            {
                CasterAlive = casterIsAlive
            };
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                alive: () => targetIsAlive);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                casterPrerequisite: livingPrerequisite.Id,
                targetPrerequisite: deadPrerequisite.Id);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();

            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            Assert.Equal(
                expectedInvocation ? 1 : 0,
                Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData.Count);
            Assert.Empty(context.Packets.OfType<Server07F8>());
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void UnitApply_IsPlayerChecksCasterAndTargetIdentity(
            bool casterPrerequisite,
            bool playerUnit,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            IUnitEntity caster = casterPrerequisite && !playerUnit
                ? context.CreateTarget(50u).Object
                : context.Caster.Object;
            IUnitEntity target = !casterPrerequisite && playerUnit
                ? context.CreatePlayerTarget(10u).Object
                : context.CreateTarget(10u).Object;
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                casterPrerequisite: casterPrerequisite ? prerequisite.Id : 0u,
                targetPrerequisite: casterPrerequisite ? 0u : prerequisite.Id);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                caster,
                effect,
                (SpellEffectTargetFlags.Target, target));

            spell.ExecuteForTest();

            Assert.Equal(expectedInvocation ? 1 : 0, context.InvocationAttempts);
            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
        }

        [Theory]
        [InlineData(590u, 6_540u, true)]
        [InlineData(590u, 6_541u, false)]
        [InlineData(3208u, 0u, true)]
        [InlineData(3208u, 6_540u, false)]
        public void UnitApply_IsCreatureBuildRowsFilterBeforeCostAndHandler(
            uint prerequisiteId,
            uint creatureId,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = prerequisiteId switch
            {
                590u => CreateEntry(
                    prerequisiteId,
                    EvaluationMode.EvaluateAND,
                    (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 6_540u, 2_446u)),
                3208u => CreateEntry(
                    prerequisiteId,
                    EvaluationMode.EvaluateAND,
                    (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 0u, 0u)),
                _ => throw new ArgumentOutOfRangeException(nameof(prerequisiteId))
            };
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                creatureId: () => creatureId);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id,
                cost: 10u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();

            Assert.Equal(expectedInvocation ? 90f : 100f, context.CasterResource1);
            Assert.Equal(expectedInvocation ? 1 : 0, context.CostMutations.Count);
            Assert.Equal(expectedInvocation ? 1 : 0, context.InvocationAttempts);
            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            Assert.Equal(
                expectedInvocation ? 1 : 0,
                Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData.Count);
            target.VerifyGet(unit => unit.CreatureId, Times.Once);
        }

        [Theory]
        [InlineData(WorldDifficulty.Normal, WorldDifficulty.Normal, WorldDifficulty.Normal, true)]
        [InlineData(WorldDifficulty.Veteran, WorldDifficulty.Veteran, WorldDifficulty.Veteran, true)]
        [InlineData(WorldDifficulty.Normal, WorldDifficulty.Normal, WorldDifficulty.Veteran, false)]
        [InlineData(WorldDifficulty.Veteran, WorldDifficulty.Normal, WorldDifficulty.Veteran, false)]
        public void UnitApply_DifficultyBuildRowsFilterBeforeCostAndHandler(
            WorldDifficulty mapDifficulty,
            WorldDifficulty casterRequirement,
            WorldDifficulty targetRequirement,
            bool expectedInvocation)
        {
            PrerequisiteEntry normal = CreateEntry(
                14409u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Normal, 0u));
            PrerequisiteEntry veteran = CreateEntry(
                14893u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Difficulty, PrerequisiteComparison.Equal, (uint)WorldDifficulty.Veteran, 0u));
            using var context = new SpellPrerequisiteContext(normal, veteran);
            context.Map.SetupGet(value => value.Difficulty).Returns(mapDifficulty);
            Mock<IUnitEntity> target = context.CreateTarget(10u);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                casterPrerequisite: casterRequirement == WorldDifficulty.Normal ? normal.Id : veteran.Id,
                targetPrerequisite: targetRequirement == WorldDifficulty.Normal ? normal.Id : veteran.Id,
                cost: 10u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();

            Assert.Equal(expectedInvocation ? 90f : 100f, context.CasterResource1);
            Assert.Equal(expectedInvocation ? 1 : 0, context.CostMutations.Count);
            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            Assert.Equal(
                expectedInvocation ? 1 : 0,
                Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData.Count);
        }

        [Fact]
        public void DelayedTargetApply_IsPlayerFiltersAtActivationTime()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IPlayer> player = context.CreatePlayerTarget(10u);
            Mock<IUnitEntity> npc = context.CreateTarget(11u);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id,
                delayTime: 100u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, player.Object),
                (SpellEffectTargetFlags.Target, npc.Object));

            spell.ExecuteForTest();
            Assert.Empty(context.Invocations);

            spell.Update(0.1d);

            Assert.Same(player.Object, Assert.Single(context.Invocations).Target);
            Assert.Single(context.Packets.OfType<Server07F8>());
        }

        [Fact]
        public void DelayedTargetApply_InCombatReevaluatesAtActivationTime()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            bool targetInCombat = false;
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                inCombat: () => targetInCombat);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id,
                delayTime: 100u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();
            Assert.Empty(context.Invocations);

            targetInCombat = true;
            spell.Update(0.1d);

            Assert.Single(context.Invocations);
            Assert.Single(context.Packets.OfType<Server07F8>());
        }

        [Fact]
        public void DelayedTargetApply_DeadStateReevaluatesAtActivationTime()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            bool targetIsAlive = true;
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                alive: () => targetIsAlive);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id,
                delayTime: 100u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();
            Assert.Empty(context.Invocations);

            targetIsAlive = false;
            spell.Update(0.1d);

            Assert.Single(context.Invocations);
            Assert.Single(context.Packets.OfType<Server07F8>());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SpellPersistence_InCombatReevaluatesCasterOrTarget(bool targetPersistence)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.InCombat, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterInCombat = true
            };
            bool targetInCombat = true;
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                inCombat: () => targetInCombat);
            if (targetPersistence)
            {
                context.Caster
                    .Setup(unit => unit.GetVisible<IWorldEntity>(target.Object.Guid))
                    .Returns(target.Object);
            }

            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u, delayTime: 100u),
                casterPersistencePrerequisite: targetPersistence ? 0u : prerequisite.Id,
                targetPersistencePrerequisite: targetPersistence ? prerequisite.Id : 0u,
                primaryTargetId: targetPersistence ? target.Object.Guid : 0u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);
            spell.Cast();
            Assert.True(spell.IsCasting);

            if (targetPersistence)
                targetInCombat = false;
            else
                context.CasterInCombat = false;

            spell.Update(0.1d);

            Assert.True(spell.IsFinishing);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SpellPersistence_DeadStateReevaluatesCasterOrTarget(bool targetPersistence)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            bool targetIsAlive = true;
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                alive: () => targetIsAlive);
            if (targetPersistence)
            {
                context.Caster
                    .Setup(unit => unit.GetVisible<IWorldEntity>(target.Object.Guid))
                    .Returns(target.Object);
            }

            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u, delayTime: 100u),
                casterPersistencePrerequisite: targetPersistence ? 0u : prerequisite.Id,
                targetPersistencePrerequisite: targetPersistence ? prerequisite.Id : 0u,
                primaryTargetId: targetPersistence ? target.Object.Guid : 0u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);
            spell.Cast();
            Assert.True(spell.IsCasting);

            if (targetPersistence)
                targetIsAlive = false;
            else
                context.CasterAlive = false;

            spell.Update(0.1d);

            Assert.True(spell.IsFinishing);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SpellPersistence_HealthPercentageReevaluatesCasterOrTarget(bool targetPersistence)
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Health, PrerequisiteComparison.GreaterThanOrEqual, 50u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterHealth = 60u,
                CasterMaxHealth = 100u
            };
            uint targetHealth = 60u;
            Mock<IUnitEntity> target = context.CreateTarget(
                42u,
                health: () => targetHealth,
                maximumHealth: () => 100u);
            if (targetPersistence)
            {
                context.Caster
                    .Setup(unit => unit.GetVisible<IWorldEntity>(target.Object.Guid))
                    .Returns(target.Object);
            }

            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u, delayTime: 100u),
                casterPersistencePrerequisite: targetPersistence ? 0u : prerequisite.Id,
                targetPersistencePrerequisite: targetPersistence ? prerequisite.Id : 0u,
                primaryTargetId: targetPersistence ? target.Object.Guid : 0u,
                targets: [(SpellEffectTargetFlags.Target, target.Object)]);
            spell.Cast();
            Assert.True(spell.IsCasting);

            if (targetPersistence)
                targetHealth = 40u;
            else
                context.CasterHealth = 40u;

            spell.Update(0.1d);

            Assert.True(spell.IsFinishing);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
        }

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
        public void MixedIsPlayerAndUnsupportedRow_NeverRegistersOrAdvertises()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u),
                (PrerequisiteType.Race, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IPlayer> target = context.CreatePlayerTarget(10u);
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
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidOrMixedUnsupportedIsCreatureRowNeverRegistersOrReadsCreatureId(
            bool mixedUnsupported)
        {
            PrerequisiteEntry prerequisite = mixedUnsupported
                ? CreateEntry(
                    3659u,
                    EvaluationMode.EvaluateAND,
                    (PrerequisiteType.Unknown47, PrerequisiteComparison.Equal, 0u, 0u),
                    (PrerequisiteType.IsCreature, PrerequisiteComparison.Equal, 0u, 0u))
                : CreateEntry(
                    1u,
                    EvaluationMode.EvaluateAND,
                    (PrerequisiteType.IsCreature, PrerequisiteComparison.GreaterThan, 5_991u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                creatureId: () => throw new InvalidOperationException(
                    "Gated creature-id read."));
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
            target.VerifyGet(unit => unit.CreatureId, Times.Never);
        }

        [Theory]
        [InlineData(22677u, 6_000u, 10_000u, true)]
        [InlineData(22677u, 6_000u, 100_000u, false)]
        [InlineData(22677u, 5_000u, 20_000u, true)]
        [InlineData(22678u, 6_000u, 10_000u, false)]
        [InlineData(22678u, 6_000u, 100_000u, true)]
        [InlineData(22678u, 5_000u, 20_000u, false)]
        public void AbsoluteAndPercentageHealthBuildRowsFilterBeforeCostAndHandler(
            uint prerequisiteId,
            uint health,
            uint maximumHealth,
            bool expectedInvocation)
        {
            PrerequisiteEntry prerequisite = prerequisiteId switch
            {
                22677u => CreateEntry(
                    prerequisiteId,
                    EvaluationMode.EvaluateAND,
                    (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThanOrEqual, 5_000u, 0u),
                    (PrerequisiteType.Health, PrerequisiteComparison.GreaterThanOrEqual, 25u, 0u)),
                22678u => CreateEntry(
                    prerequisiteId,
                    EvaluationMode.EvaluateOR,
                    (PrerequisiteType.HealthRequirement, PrerequisiteComparison.LessThan, 5_000u, 0u),
                    (PrerequisiteType.Health, PrerequisiteComparison.LessThan, 25u, 0u)),
                _ => throw new ArgumentOutOfRangeException(nameof(prerequisiteId))
            };
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                health: () => health,
                maximumHealth: () => maximumHealth);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id,
                cost: 10u);
            TestApplyPrerequisiteSpell spell = context.CreateSpell(
                effect,
                (SpellEffectTargetFlags.Target, target.Object));

            spell.ExecuteForTest();

            Assert.Equal(expectedInvocation ? 90f : 100f, context.CasterResource1);
            Assert.Equal(expectedInvocation ? 1 : 0, context.CostMutations.Count);
            Assert.Equal(expectedInvocation ? 1 : 0, context.Invocations.Count);
            Assert.Equal(
                expectedInvocation ? 1 : 0,
                Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData.Count);
        }

        [Fact]
        public void UnsupportedMixedHealthRequirementBuildRowNeverRegistersOrReadsState()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                33064u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.Unknown47, PrerequisiteComparison.NotEqual, 21_526u, 0u),
                (PrerequisiteType.HealthRequirement, PrerequisiteComparison.GreaterThan, 100u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> target = context.CreateTarget(
                10u,
                health: () => throw new InvalidOperationException("Gated current-health read."),
                maximumHealth: () => throw new InvalidOperationException("Gated max-health read."));
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
            target.VerifyGet(unit => unit.Health, Times.Never);
            target.VerifyGet(unit => unit.MaxHealth, Times.Never);
        }

        [Theory]
        [InlineData(PrerequisiteType.InCombat, 0)]
        [InlineData(PrerequisiteType.InCombat, 1)]
        [InlineData(PrerequisiteType.InCombat, 2)]
        [InlineData(PrerequisiteType.DeadState, 0)]
        [InlineData(PrerequisiteType.DeadState, 1)]
        [InlineData(PrerequisiteType.DeadState, 2)]
        [InlineData(PrerequisiteType.IsPlayer, 0)]
        [InlineData(PrerequisiteType.IsPlayer, 1)]
        [InlineData(PrerequisiteType.IsPlayer, 2)]
        [InlineData(PrerequisiteType.IsCreature, 0)]
        [InlineData(PrerequisiteType.IsCreature, 1)]
        [InlineData(PrerequisiteType.IsCreature, 2)]
        [InlineData(PrerequisiteType.HealthRequirement, 0)]
        [InlineData(PrerequisiteType.HealthRequirement, 1)]
        [InlineData(PrerequisiteType.HealthRequirement, 2)]
        [InlineData(PrerequisiteType.Health, 0)]
        [InlineData(PrerequisiteType.Health, 1)]
        [InlineData(PrerequisiteType.Health, 2)]
        public void UnitSafeEffectPersistenceOrSuspendPrerequisite_RemainsGated(
            PrerequisiteType type,
            int field)
        {
            PrerequisiteComparison comparison = type is PrerequisiteType.HealthRequirement or PrerequisiteType.Health
                ? PrerequisiteComparison.GreaterThanOrEqual
                : PrerequisiteComparison.Equal;
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (type, comparison, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterInCombat = true
            };
            Mock<IUnitEntity> target = context.CreateTarget(10u, inCombat: () => true);
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
        public void AuraTargetApply_IsPlayerReevaluatesReboundIdentity()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.IsPlayer, PrerequisiteComparison.Equal, 0u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite);
            Mock<IUnitEntity> npc = context.CreateTarget(42u);
            Mock<IPlayer> player = context.CreatePlayerTarget(42u);
            IUnitEntity visible = npc.Object;
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                targetPrerequisite: prerequisite.Id);
            TestApplyPrerequisiteAura spell = context.CreateAura(
                effect,
                42u,
                () => visible);

            spell.ExecuteForTest();
            spell.Update(0.1d);
            Assert.Empty(context.Invocations);

            visible = player.Object;
            spell.Update(0.1d);
            spell.Update(0.1d);
            spell.Update(0.1d);

            Assert.Same(player.Object, Assert.Single(context.Invocations).Target);
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
        public void DeadOnlyCasterPersistence_RejectsLivingCasterBeforeStart()
        {
            PrerequisiteEntry prerequisite = CreateEntry(
                1u,
                EvaluationMode.EvaluateAND,
                (PrerequisiteType.DeadState, PrerequisiteComparison.Equal, 1u, 0u));
            using var context = new SpellPrerequisiteContext(prerequisite)
            {
                CasterAlive = true
            };
            TestApplyPrerequisiteSpell spell = context.CreatePersistenceSpell(
                CreateEffect(1u, cost: 10u),
                casterPersistencePrerequisite: prerequisite.Id,
                targets: [(SpellEffectTargetFlags.Target, context.Caster.Object)]);

            spell.Cast();
            spell.Update(0d);
            spell.LateUpdate(0d);

            Assert.True(spell.IsFinished);
            ServerSpellCastResult result = Assert.Single(
                context.SessionPackets.OfType<ServerSpellCastResult>());
            Assert.Equal(CastResult.PrereqCasterPersistence, result.CastResult);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
            Assert.Empty(context.Packets.OfType<ServerSpellFinish>());
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

        private static void AssertTargetCastFailure(
            SpellPrerequisiteContext context,
            TestApplyPrerequisiteSpell spell)
        {
            Assert.True(spell.IsFinishing);
            ServerSpellCastResult result = Assert.Single(
                context.SessionPackets.OfType<ServerSpellCastResult>());
            Assert.Equal(CastResult.PrereqTargetCast, result.CastResult);
            Assert.Equal(0x0119, (int)result.CastResult);
            Assert.Empty(context.CostMutations);
            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<ServerSpellStart>());
            Assert.Empty(context.Packets.OfType<ServerSpellGo>());
        }

        public enum TargetCastUnsupportedShape
        {
            Missing,
            Mixed,
            Malformed
        }

        public enum TargetCastResolutionFailure
        {
            Missing,
            NonUnit,
            LookupThrows
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
            public uint CasterCreatureId { get; set; }
            public uint CasterHealth { get; set; } = 100u;
            public uint CasterMaxHealth { get; set; } = 100u;
            public Faction CasterFaction { get; set; } = Faction.Exile;
            public bool CasterAlive { get; set; } = true;
            public bool CasterInCombat { get; set; }
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
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckInCombat>(PrerequisiteType.InCombat);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckDeadState>(PrerequisiteType.DeadState);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckIsPlayer>(PrerequisiteType.IsPlayer);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckIsCreature>(PrerequisiteType.IsCreature);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckHealth>(PrerequisiteType.Health);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckHealthRequirement>(PrerequisiteType.HealthRequirement);
                services.AddKeyedTransient<IPrerequisiteCheck, PrerequisiteCheckDifficulty>(PrerequisiteType.Difficulty);
                services.AddSingleton<PrerequisiteManager>();
                services.AddSingleton<IPrerequisiteManager>(provider => provider.GetRequiredService<PrerequisiteManager>());
                serviceProvider = services.BuildServiceProvider();
                LegacyServiceProvider.Provider = serviceProvider;

                var spellManager = new Mock<ISpellManager>();
                Caster.SetupGet(entity => entity.Guid).Returns(7u);
                Caster.SetupGet(entity => entity.IsAlive).Returns(() => CasterAlive);
                Caster.SetupGet(entity => entity.InWorld).Returns(true);
                Caster.SetupGet(entity => entity.Map).Returns(Map.Object);
                Caster.SetupGet(entity => entity.IsLoading).Returns(false);
                Caster.SetupGet(entity => entity.Session).Returns(Session.Object);
                Caster.SetupGet(entity => entity.SpellManager).Returns(spellManager.Object);
                Caster.SetupGet(entity => entity.CreatureId).Returns(() => CasterCreatureId);
                Caster.SetupGet(entity => entity.Level).Returns(() =>
                {
                    CasterLevelReads++;
                    return CasterLevel;
                });
                Caster.SetupGet(entity => entity.Faction1).Returns(() => CasterFaction);
                Caster.SetupGet(entity => entity.InCombat).Returns(() => CasterInCombat);
                Caster.SetupGet(entity => entity.Health).Returns(() => CasterHealth);
                Caster.SetupGet(entity => entity.MaxHealth).Returns(() => CasterMaxHealth);
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
                Func<IBaseMap> map = null,
                Func<bool> inCombat = null,
                Func<uint> health = null,
                Func<uint> maximumHealth = null,
                Func<uint> creatureId = null)
            {
                var target = new Mock<IUnitEntity>();
                ConfigureTarget(
                    target,
                    guid,
                    level,
                    faction,
                    vital,
                    alive,
                    inWorld,
                    map,
                    inCombat,
                    health,
                    maximumHealth,
                    creatureId);
                return target;
            }

            public Mock<IPlayer> CreatePlayerTarget(
                uint guid,
                Func<bool> alive = null,
                Func<bool> inWorld = null,
                Func<IBaseMap> map = null,
                Func<uint> creatureId = null)
            {
                var target = new Mock<IPlayer>();
                ConfigureTarget(
                    target,
                    guid,
                    alive: alive,
                    inWorld: inWorld,
                    map: map,
                    creatureId: creatureId);
                return target;
            }

            private void ConfigureTarget<T>(
                Mock<T> target,
                uint guid,
                Func<uint> level = null,
                Func<Faction> faction = null,
                Func<Vital, float?> vital = null,
                Func<bool> alive = null,
                Func<bool> inWorld = null,
                Func<IBaseMap> map = null,
                Func<bool> inCombat = null,
                Func<uint> health = null,
                Func<uint> maximumHealth = null,
                Func<uint> creatureId = null)
                where T : class, IUnitEntity
            {
                target.SetupGet(entity => entity.Guid).Returns(guid);
                target.SetupGet(entity => entity.CreatureId).Returns(() => creatureId?.Invoke() ?? 0u);
                target.SetupGet(entity => entity.IsAlive).Returns(() => alive?.Invoke() ?? true);
                target.SetupGet(entity => entity.InWorld).Returns(() => inWorld?.Invoke() ?? true);
                target.SetupGet(entity => entity.Map).Returns(() => map?.Invoke() ?? Map.Object);
                target.SetupGet(entity => entity.Level).Returns(() => level?.Invoke() ?? 1u);
                target.SetupGet(entity => entity.Faction1).Returns(() => faction?.Invoke() ?? Faction.Dominion);
                target.SetupGet(entity => entity.InCombat).Returns(() => inCombat?.Invoke() ?? false);
                target.SetupGet(entity => entity.Health).Returns(() => health?.Invoke() ?? 1u);
                target.SetupGet(entity => entity.MaxHealth).Returns(() => maximumHealth?.Invoke() ?? 1u);
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
            }

            public TestApplyPrerequisiteSpell CreateSpell(
                Spell4EffectsEntry effect,
                params (SpellEffectTargetFlags Flags, IUnitEntity Entity)[] targets)
            {
                return CreateSpell(Caster.Object, effect, targets);
            }

            public TestApplyPrerequisiteSpell CreateSpell(
                IUnitEntity caster,
                Spell4EffectsEntry effect,
                params (SpellEffectTargetFlags Flags, IUnitEntity Entity)[] targets)
            {
                var spell = new TestApplyPrerequisiteSpell(
                    caster,
                    CreateParameters(CastMethod.Normal, effect),
                    () => targets);
                spell.SetStatus(SpellStatus.Casting);
                return spell;
            }

            public TestApplyPrerequisiteSpell CreateCastSpell(
                Spell4EffectsEntry effect,
                PrerequisiteEntry casterCastPrerequisite = null,
                PrerequisiteEntry targetCastPrerequisite = null,
                uint primaryTargetId = 0u,
                uint? targetCastPrerequisiteId = null,
                IUnitEntity caster = null,
                params (SpellEffectTargetFlags Flags, IUnitEntity Entity)[] targets)
            {
                SpellParameters parameters = CreateParameters(
                    CastMethod.Normal,
                    effect,
                    casterCastPrerequisite,
                    targetCastPrerequisite);
                parameters.PrimaryTargetId = primaryTargetId;
                if (targetCastPrerequisiteId.HasValue)
                {
                    parameters.SpellInfo.Entry.PrerequisiteIdTargetCast =
                        targetCastPrerequisiteId.Value;
                }

                return new TestApplyPrerequisiteSpell(
                    caster ?? Caster.Object,
                    parameters,
                    () => targets);
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
                return CreateAura(effect, target.Guid, () => target);
            }

            public TestApplyPrerequisiteAura CreateAura(
                Spell4EffectsEntry effect,
                uint targetGuid,
                Func<IUnitEntity> target)
            {
                Caster.Setup(entity => entity.GetVisible<IUnitEntity>(targetGuid))
                    .Returns(target);
                SpellParameters parameters = CreateParameters(CastMethod.Aura, effect);
                parameters.PrimaryTargetId = targetGuid;
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
                Spell4EffectsEntry effect,
                PrerequisiteEntry casterCastPrerequisite = null,
                PrerequisiteEntry targetCastPrerequisite = null)
            {
                var baseInfo = new Mock<ISpellBaseInfo>();
                baseInfo.SetupGet(info => info.Entry).Returns(new Spell4BaseEntry
                {
                    CastMethod = (uint)castMethod
                });
                var spellInfo = new Mock<ISpellInfo>();
                spellInfo.SetupGet(info => info.Entry).Returns(new Spell4Entry
                {
                    Id                       = 123u,
                    SpellDuration            = castMethod == CastMethod.Aura ? 1_000u : 0u,
                    PrerequisiteIdCasterCast = casterCastPrerequisite?.Id ?? 0u,
                    PrerequisiteIdTargetCast = targetCastPrerequisite?.Id ?? 0u
                });
                spellInfo.SetupGet(info => info.BaseInfo).Returns(baseInfo.Object);
                spellInfo.SetupGet(info => info.Effects).Returns([effect]);
                spellInfo.SetupGet(info => info.Telegraphs).Returns([]);
                spellInfo.SetupGet(info => info.PrerequisiteRunners).Returns([]);
                spellInfo.SetupGet(info => info.CasterCastPrerequisite)
                    .Returns(casterCastPrerequisite);
                spellInfo.SetupGet(info => info.TargetCastPrerequisites)
                    .Returns(targetCastPrerequisite);
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
