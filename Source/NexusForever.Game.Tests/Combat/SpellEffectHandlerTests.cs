using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Quest;
using NexusForever.Game.Static.Combat;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Combat;
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
        public void HandleEffectVitalModifier_HandlerExists()
        {
            var methods = typeof(SpellHandler).GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(SpellEffectHandlerAttribute), false)
                    .Cast<SpellEffectHandlerAttribute>()
                    .Any(a => a.SpellEffectType == SpellEffectType.VitalModifier));

            Assert.Single(methods);
        }

        [Theory]
        [InlineData(25, 50f, 100f, 75f, 25f)]
        [InlineData(-25, 50f, 100f, 25f, -25f)]
        [InlineData(int.MinValue, 100f, 100f, 0f, -100f)]
        public void HandleEffectVitalModifier_SignedFixedAmountMutatesAndLogsActualDelta(
            int amount,
            float initial,
            float maximum,
            float expected,
            float expectedActual)
        {
            var caster = new Mock<IUnitEntity>();
            caster.SetupGet(entity => entity.Guid).Returns(41u);
            var target = new VitalTargetContext(Vital.Resource1, initial, maximum);
            ISpell spell = CreateVitalModifierSpell(caster.Object, 123u);
            var info = CreateVitalModifierInfo(Vital.Resource1, amount);

            SpellHandler.HandleEffectVitalModifier(spell, target.Target.Object, info);

            Assert.False(info.DropEffect);
            Assert.Equal(expected, target.Value(Vital.Resource1));
            Assert.Equal((Vital.Resource1, (float)amount), Assert.Single(target.Mutations));
            CombatLogVitalModifier combatLog = Assert.IsType<CombatLogVitalModifier>(Assert.Single(info.CombatLogs));
            Assert.Equal(expectedActual, combatLog.Amount);
            Assert.Equal(Vital.Resource1, combatLog.VitalModified);
            Assert.True(combatLog.BShowCombatLog);
            Assert.Equal(41u, combatLog.CastData.CasterId);
            Assert.Equal(42u, combatLog.CastData.TargetId);
            Assert.Equal(123u, combatLog.CastData.SpellId);
            Assert.Equal(CombatResult.Hit, combatLog.CastData.CombatResult);
            target.Target.Verify(entity => entity.TryModifyVital(
                Vital.Resource1,
                (float)amount,
                caster.Object), Times.Once);
        }

        [Theory]
        [InlineData(0, 50f, 100f, 0)]
        [InlineData(10, 100f, 100f, 0)]
        public void HandleEffectVitalModifier_NoActualChangeIsDroppedWithoutCombatLog(
            int amount,
            float initial,
            float maximum,
            int expectedMutationAttempts)
        {
            var target = new VitalTargetContext(Vital.Resource1, initial, maximum);
            var info = CreateVitalModifierInfo(Vital.Resource1, amount);

            SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Target.Object,
                info);

            Assert.True(info.DropEffect);
            Assert.Equal(initial, target.Value(Vital.Resource1));
            Assert.Equal(expectedMutationAttempts, target.Mutations.Count);
            Assert.Empty(info.CombatLogs);
        }

        [Fact]
        public void HandleEffectVitalModifier_HealthDeathIsIdempotentAndCannotRevive()
        {
            var target = new VitalTargetContext(Vital.Health, 20f, 100f);
            ISpell spell = CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u);

            var lethal = CreateVitalModifierInfo(Vital.Health, -50);
            SpellHandler.HandleEffectVitalModifier(spell, target.Target.Object, lethal);

            Assert.False(lethal.DropEffect);
            Assert.Equal(0f, target.Value(Vital.Health));
            Assert.Equal(-20f, Assert.IsType<CombatLogVitalModifier>(Assert.Single(lethal.CombatLogs)).Amount);

            var repeated = CreateVitalModifierInfo(Vital.Health, -50);
            SpellHandler.HandleEffectVitalModifier(spell, target.Target.Object, repeated);

            Assert.True(repeated.DropEffect);
            Assert.Empty(repeated.CombatLogs);
            Assert.Equal(0f, target.Value(Vital.Health));

            var revive = CreateVitalModifierInfo(Vital.Health, 10);
            SpellHandler.HandleEffectVitalModifier(spell, target.Target.Object, revive);

            Assert.True(revive.DropEffect);
            Assert.Empty(revive.CombatLogs);
            Assert.Equal(0f, target.Value(Vital.Health));
        }

        [Theory]
        [InlineData(Vital.KineticCell, Vital.Resource1)]
        [InlineData(Vital.StalkerB, Vital.Resource1)]
        [InlineData(Vital.MedicCore, Vital.Resource1)]
        [InlineData(Vital.Volatility, Vital.Resource1)]
        [InlineData(Vital.StalkerA, Vital.Resource3)]
        [InlineData(Vital.SpellSurge, Vital.Resource4)]
        public void HandleEffectVitalModifier_AliasesUseExistingVitalPolicy(
            Vital vital,
            Vital canonicalVital)
        {
            var target = new VitalTargetContext(vital, 10f, 100f);
            var info = CreateVitalModifierInfo(vital, 5);

            SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Target.Object,
                info);

            Assert.False(info.DropEffect);
            Assert.Equal(15f, target.Value(canonicalVital));
            Assert.Equal((vital, 5f), Assert.Single(target.Mutations));
            Assert.Equal(vital, Assert.IsType<CombatLogVitalModifier>(Assert.Single(info.CombatLogs)).VitalModified);
        }

        [Theory]
        [InlineData(0u, false)]
        [InlineData(1u, true)]
        public void HandleEffectVitalModifier_CombatLogVisibilityUsesDataBits09(
            uint rawVisibility,
            bool expectedVisibility)
        {
            var target = new VitalTargetContext(Vital.Resource1, 10f, 100f);
            var info = CreateVitalModifierInfo(Vital.Resource1, 5, rawVisibility);

            SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Target.Object,
                info);

            CombatLogVitalModifier combatLog = Assert.IsType<CombatLogVitalModifier>(Assert.Single(info.CombatLogs));
            Assert.Equal(expectedVisibility, combatLog.BShowCombatLog);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        public void HandleEffectVitalModifier_BreathRowsFailClosedWithoutMutation(int amount)
        {
            var target = new Mock<IUnitEntity>();
            var info = CreateVitalModifierInfo(Vital.Breath, amount);

            SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Object,
                info);

            Assert.True(info.DropEffect);
            Assert.Empty(info.CombatLogs);
            target.Verify(entity => entity.TryGetVitalValue(
                It.IsAny<Vital>(), out It.Ref<float>.IsAny), Times.Never);
            target.Verify(entity => entity.TryModifyVital(
                It.IsAny<Vital>(), It.IsAny<float>(), It.IsAny<IUnitEntity>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        public void HandleEffectVitalModifier_FormulaOrRangeRowsFailClosedWithoutMutation(
            int unsupportedField)
        {
            var target = new Mock<IUnitEntity>();
            var info = CreateVitalModifierInfo(Vital.Resource1, 5);
            switch (unsupportedField)
            {
                case 0:
                    info.Entry.DataBits02++;
                    break;
                case 3:
                    info.Entry.DataBits03 = 1u;
                    break;
                case 4:
                    info.Entry.DataBits04 = 1u;
                    break;
                case 5:
                    info.Entry.DataBits05 = 1u;
                    break;
                case 6:
                    info.Entry.DataBits06 = 1u;
                    break;
                case 7:
                    info.Entry.DataBits07 = 1u;
                    break;
                case 8:
                    info.Entry.DataBits08 = 1u;
                    break;
                case 9:
                    info.Entry.DataBits09 = 2u;
                    break;
                case 10:
                    info.Entry.ParameterType[0] = SpellEffectParameterType.Weapon;
                    break;
                case 11:
                    info.Entry.ParameterValue[0] = 1f;
                    break;
                case 12:
                    info.Entry.ParameterValue[0] = float.NaN;
                    break;
                case 13:
                    info.Entry.ParameterType = null;
                    break;
                case 14:
                    info.Entry.ParameterValue = new float[3];
                    break;
            }

            SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Object,
                info);

            Assert.True(info.DropEffect);
            Assert.Empty(info.CombatLogs);
            target.Verify(entity => entity.TryGetVitalValue(
                It.IsAny<Vital>(), out It.Ref<float>.IsAny), Times.Never);
            target.Verify(entity => entity.TryModifyVital(
                It.IsAny<Vital>(), It.IsAny<float>(), It.IsAny<IUnitEntity>()), Times.Never);
        }

        [Fact]
        public void HandleEffectVitalModifier_InexactFixedAmountFailsClosedWithoutMutation()
        {
            var target = new Mock<IUnitEntity>();
            var info = CreateVitalModifierInfo(Vital.Resource1, int.MaxValue);

            SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Object,
                info);

            Assert.True(info.DropEffect);
            Assert.Empty(info.CombatLogs);
            target.Verify(entity => entity.TryGetVitalValue(
                It.IsAny<Vital>(), out It.Ref<float>.IsAny), Times.Never);
            target.Verify(entity => entity.TryModifyVital(
                It.IsAny<Vital>(), It.IsAny<float>(), It.IsAny<IUnitEntity>()), Times.Never);
        }

        [Fact]
        public void HandleEffectVitalModifier_PostMutationNotificationExceptionIsReconciled()
        {
            var target = new VitalTargetContext(Vital.Resource1, 10f, 100f)
            {
                ThrowAfterMutation = true
            };
            var info = CreateVitalModifierInfo(Vital.Resource1, 5);

            Exception exception = Record.Exception(() => SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Target.Object,
                info));

            Assert.Null(exception);
            Assert.False(info.DropEffect);
            Assert.Equal(15f, target.Value(Vital.Resource1));
            Assert.Equal(5f, Assert.IsType<CombatLogVitalModifier>(Assert.Single(info.CombatLogs)).Amount);
        }

        [Fact]
        public void HandleEffectVitalModifier_UnconfirmedMutationExceptionFailsClosed()
        {
            var target = new VitalTargetContext(Vital.Resource1, 10f, 100f)
            {
                ThrowBeforeMutation = true
            };
            var info = CreateVitalModifierInfo(Vital.Resource1, 5);

            Exception exception = Record.Exception(() => SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Target.Object,
                info));

            Assert.Null(exception);
            Assert.True(info.DropEffect);
            Assert.Equal(10f, target.Value(Vital.Resource1));
            Assert.Empty(info.CombatLogs);
        }

        [Fact]
        public void HandleEffectVitalModifier_RejectedMutationFailsClosedAfterOneAttempt()
        {
            var target = new VitalTargetContext(Vital.Resource1, 10f, 100f)
            {
                RejectMutation = true
            };
            var info = CreateVitalModifierInfo(Vital.Resource1, 5);

            SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Target.Object,
                info);

            Assert.True(info.DropEffect);
            Assert.Equal(10f, target.Value(Vital.Resource1));
            Assert.Empty(target.Mutations);
            Assert.Empty(info.CombatLogs);
            target.Target.Verify(entity => entity.TryModifyVital(
                Vital.Resource1,
                5f,
                It.IsAny<IUnitEntity>()), Times.Once);
        }

        [Fact]
        public void HandleEffectVitalModifier_UnexpectedPostStateFailsClosedWithoutCombatLog()
        {
            var target = new VitalTargetContext(Vital.Resource1, 10f, 100f)
            {
                MutationDeltaOverride = 3f
            };
            var info = CreateVitalModifierInfo(Vital.Resource1, 5);

            SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Target.Object,
                info);

            Assert.True(info.DropEffect);
            Assert.Equal(13f, target.Value(Vital.Resource1));
            Assert.Empty(info.CombatLogs);
        }

        [Fact]
        public void HandleEffectVitalModifier_HealthZeroNotificationExceptionCannotBeReconciled()
        {
            var target = new VitalTargetContext(Vital.Health, 10f, 100f)
            {
                ThrowAfterMutation = true
            };
            var info = CreateVitalModifierInfo(Vital.Health, -10);

            Exception exception = Record.Exception(() => SpellHandler.HandleEffectVitalModifier(
                CreateVitalModifierSpell(Mock.Of<IUnitEntity>(), 123u),
                target.Target.Object,
                info));

            Assert.Null(exception);
            Assert.True(info.DropEffect);
            Assert.Equal(0f, target.Value(Vital.Health));
            Assert.Empty(info.CombatLogs);
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
        public void HandleEffectQuestAdvanceObjective_AdvancesValidatedPlayerObjective()
        {
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            var info = new SpellTargetInfo.SpellTargetEffectInfo(1u, new Spell4EffectsEntry
            {
                EffectType = SpellEffectType.QuestAdvanceObjective,
                DataBits00 = 10_643u,
                DataBits01 = 7u
            });

            SpellHandler.HandleEffectQuestAdvanceObjective(Mock.Of<ISpell>(), player.Object, info);

            questManager.Verify(manager => manager.QuestAchieveObjective(10_643, 7), Times.Once);
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(0x8000u, 0u)]
        [InlineData(100u, 255u)]
        [InlineData(100u, 256u)]
        public void HandleEffectQuestAdvanceObjective_InvalidPackedIdentifiersFailClosed(
            uint questId,
            uint objectiveIndex)
        {
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            var info = new SpellTargetInfo.SpellTargetEffectInfo(1u, new Spell4EffectsEntry
            {
                EffectType = SpellEffectType.QuestAdvanceObjective,
                DataBits00 = questId,
                DataBits01 = objectiveIndex
            });

            SpellHandler.HandleEffectQuestAdvanceObjective(Mock.Of<ISpell>(), player.Object, info);

            questManager.Verify(
                manager => manager.QuestAchieveObjective(It.IsAny<ushort>(), It.IsAny<byte>()),
                Times.Never);
        }

        [Fact]
        public void HandleEffectQuestAdvanceObjective_NonPlayerTargetIsIgnored()
        {
            var info = new SpellTargetInfo.SpellTargetEffectInfo(1u, new Spell4EffectsEntry
            {
                EffectType = SpellEffectType.QuestAdvanceObjective,
                DataBits00 = 100u,
                DataBits01 = 1u
            });

            SpellHandler.HandleEffectQuestAdvanceObjective(
                Mock.Of<ISpell>(),
                Mock.Of<IUnitEntity>(),
                info);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void HandleEffectQuestAdvanceObjective_ExpectedQuestRejectionIsContained(
            bool invalidStaticReference)
        {
            var questManager = new Mock<IQuestManager>();
            questManager
                .Setup(manager => manager.QuestAchieveObjective(100, 1))
                .Throws(invalidStaticReference
                    ? new ArgumentException("Invalid quest.")
                    : new QuestException("Quest is not active."));
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            var info = new SpellTargetInfo.SpellTargetEffectInfo(1u, new Spell4EffectsEntry
            {
                EffectType = SpellEffectType.QuestAdvanceObjective,
                DataBits00 = 100u,
                DataBits01 = 1u
            });

            SpellHandler.HandleEffectQuestAdvanceObjective(Mock.Of<ISpell>(), player.Object, info);

            questManager.Verify(manager => manager.QuestAchieveObjective(100, 1), Times.Once);
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

        private static ISpell CreateVitalModifierSpell(IUnitEntity caster, uint spellId)
        {
            var spellInfo = new Mock<ISpellInfo>();
            spellInfo.SetupGet(info => info.Entry).Returns(new Spell4Entry { Id = spellId });
            var parameters = new SpellParameters
            {
                SpellInfo = spellInfo.Object
            };
            var spell = new Mock<ISpell>();
            spell.SetupGet(value => value.Caster).Returns(caster);
            spell.SetupGet(value => value.Parameters).Returns(parameters);
            return spell.Object;
        }

        private static SpellTargetInfo.SpellTargetEffectInfo CreateVitalModifierInfo(
            Vital vital,
            int amount,
            uint showCombatLog = 1u)
        {
            uint rawAmount = unchecked((uint)amount);
            return new SpellTargetInfo.SpellTargetEffectInfo(1u, new Spell4EffectsEntry
            {
                Id          = 456u,
                SpellId     = 123u,
                EffectType  = SpellEffectType.VitalModifier,
                DataBits00  = (uint)vital,
                DataBits01  = rawAmount,
                DataBits02  = rawAmount,
                DataBits09  = showCombatLog,
                ParameterType  = new SpellEffectParameterType[4],
                ParameterValue = new float[4]
            });
        }

        private sealed class VitalTargetContext
        {
            public Mock<IUnitEntity> Target { get; } = new();
            public List<(Vital Vital, float Delta)> Mutations { get; } = [];

            public bool ThrowBeforeMutation { get; init; }
            public bool ThrowAfterMutation { get; init; }
            public bool RejectMutation { get; init; }
            public float? MutationDeltaOverride { get; init; }

            private readonly Dictionary<Vital, float> values = [];
            private readonly Dictionary<Vital, float> maxima = [];
            private bool alive = true;

            public VitalTargetContext(Vital vital, float value, float maximum)
            {
                Vital canonicalVital = Canonical(vital);
                values[canonicalVital] = value;
                maxima[canonicalVital] = maximum;
                if (canonicalVital == Vital.Health && value == 0f)
                    alive = false;

                Target.SetupGet(entity => entity.Guid).Returns(42u);
                Target.SetupGet(entity => entity.IsAlive).Returns(() => alive);
                Target.Setup(entity => entity.TryGetVitalValue(
                        It.IsAny<Vital>(),
                        out It.Ref<float>.IsAny))
                    .Returns(new TryGetVitalValue((Vital requestedVital, out float current) =>
                        values.TryGetValue(Canonical(requestedVital), out current)));
                Target.Setup(entity => entity.TryGetVitalMaximum(
                        It.IsAny<Vital>(),
                        out It.Ref<float>.IsAny))
                    .Returns(new TryGetVitalMaximum((Vital requestedVital, out float maximumValue) =>
                        maxima.TryGetValue(Canonical(requestedVital), out maximumValue)));
                Target.Setup(entity => entity.TryModifyVital(
                        It.IsAny<Vital>(),
                        It.IsAny<float>(),
                        It.IsAny<IUnitEntity>()))
                    .Returns((Vital requestedVital, float delta, IUnitEntity _) =>
                    {
                        if (ThrowBeforeMutation)
                            throw new InvalidOperationException("Test mutation failure.");

                        if (RejectMutation)
                            return false;

                        Vital canonical = Canonical(requestedVital);
                        if (!values.TryGetValue(canonical, out float current)
                            || !maxima.TryGetValue(canonical, out float maximum)
                            || (canonical == Vital.Health && delta > 0f && !alive))
                            return false;

                        Mutations.Add((requestedVital, delta));
                        float appliedDelta = MutationDeltaOverride ?? delta;
                        float next = Math.Clamp(current + appliedDelta, 0f, maximum);
                        values[canonical] = next;
                        if (canonical == Vital.Health && current > 0f && next == 0f)
                            alive = false;

                        if (ThrowAfterMutation && next != current)
                            throw new InvalidOperationException("Test post-mutation notification failure.");

                        return true;
                    });
            }

            public float Value(Vital vital)
            {
                return values[Canonical(vital)];
            }

            private static Vital Canonical(Vital vital)
            {
                return vital switch
                {
                    Vital.KineticCell or Vital.StalkerB or Vital.MedicCore or Vital.Volatility => Vital.Resource1,
                    Vital.StalkerA                                                             => Vital.Resource3,
                    Vital.SpellSurge                                                           => Vital.Resource4,
                    _                                                                          => vital
                };
            }
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
        private delegate bool TryGetVitalMaximum(Vital vital, out float maximum);
    }
}
