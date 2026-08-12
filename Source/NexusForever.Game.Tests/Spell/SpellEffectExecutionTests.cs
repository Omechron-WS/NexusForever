using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Combat;
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
    public sealed class SpellEffectExecutionTests
    {
        [Fact]
        public void ImmediateEffect_ConsumesOnceBeforeMutatingMultipleTargets()
        {
            using var context = new EffectExecutionContext(100f);
            Mock<IUnitEntity> secondTarget = context.CreateTarget(8u);
            context.SetTargets(context.Player.Object, secondTarget.Object);
            var valuesObservedByHandlers = new List<float>();
            context.Handler = (_, _, _) =>
                valuesObservedByHandlers.Add(context.Values[Vital.Resource1]);
            Spell4EffectsEntry effect = CreateEffect(1u, 10u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();

            Assert.Equal(90f, context.Values[Vital.Resource1]);
            Assert.Equal([(Vital.Resource1, -10f)], context.Mutations);
            Assert.Equal([90f, 90f], valuesObservedByHandlers);
            Assert.Equal(2, context.Invocations.Count);
        }

        [Theory]
        [InlineData(false, 1, 90f)]
        [InlineData(true, 3, 70f)]
        public void DelayedOrPeriodicEffect_ConsumesOncePerActualRowInvocation(
            bool periodic,
            int expectedInvocations,
            float expectedValue)
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                10u,
                delayTime: periodic ? 0u : 100u,
                tickTime: periodic ? 100u : 0u,
                durationTime: periodic ? 300u : 0u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();
            Assert.Empty(context.Mutations);

            spell.Update(periodic ? 0.3d : 0.1d);

            Assert.Equal(expectedValue, context.Values[Vital.Resource1]);
            Assert.Equal(expectedInvocations, context.Invocations.Count);
            Assert.Equal(expectedInvocations, context.Mutations.Count);
            Assert.All(context.Mutations, mutation =>
                Assert.Equal((Vital.Resource1, -10f), mutation));
        }

        [Theory]
        [InlineData(false, 1, 90f)]
        [InlineData(true, 3, 70f)]
        public void VitalModifier_DelayedOrPeriodicActivationMutatesAndPublishesCombatLog(
            bool periodic,
            int expectedInvocations,
            float expectedValue)
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateVitalModifierEffect(
                21u,
                Vital.Resource1,
                -10,
                delayTime: periodic ? 0u : 100u,
                tickTime: periodic ? 100u : 0u,
                durationTime: periodic ? 300u : 0u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();
            Assert.Empty(context.Mutations);
            context.VisiblePackets.Clear();

            spell.Update(periodic ? 0.3d : 0.1d);

            Assert.Equal(expectedValue, context.Values[Vital.Resource1]);
            Assert.Equal(expectedInvocations, context.Mutations.Count);
            Assert.All(context.Mutations, mutation =>
                Assert.Equal((Vital.Resource1, -10f), mutation));
            CombatLogVitalModifier[] combatLogs = context.VisiblePackets
                .OfType<ServerCombatLog>()
                .Select(packet => Assert.IsType<CombatLogVitalModifier>(packet.CombatLog))
                .ToArray();
            Assert.Equal(expectedInvocations, combatLogs.Length);
            Assert.All(combatLogs, combatLog =>
            {
                Assert.Equal(-10f, combatLog.Amount);
                Assert.Equal(Vital.Resource1, combatLog.VitalModified);
                Assert.True(combatLog.BShowCombatLog);
                Assert.Equal(7u, combatLog.CastData.CasterId);
                Assert.Equal(7u, combatLog.CastData.TargetId);
                Assert.Equal(123u, combatLog.CastData.SpellId);
            });
            Assert.Equal(expectedInvocations, context.VisiblePackets.OfType<Server07F8>().Count());
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(5, true)]
        public void VitalModifier_FailedOrNoChangeActivationPublishesNoLogOrFollowUp(
            int amount,
            bool unsupportedShape)
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateVitalModifierEffect(
                22u,
                Vital.Resource1,
                amount,
                delayTime: 100u);
            if (unsupportedShape)
                effect.DataBits03 = 1u;
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();
            context.VisiblePackets.Clear();
            spell.Update(0.1d);

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Empty(context.Mutations);
            Assert.DoesNotContain(context.VisiblePackets, packet => packet is ServerCombatLog);
            Assert.DoesNotContain(context.VisiblePackets, packet => packet is Server07F8);
        }

        [Fact]
        public void VitalModifier_PostMutationNotificationExceptionStillPublishesConfirmedActivation()
        {
            using var context = new EffectExecutionContext(10f);
            context.ThrowAfterMutation = (vital, delta) =>
                vital == Vital.Resource1 && delta == 5f;
            Spell4EffectsEntry effect = CreateVitalModifierEffect(
                23u,
                Vital.Resource1,
                5,
                delayTime: 100u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();
            context.VisiblePackets.Clear();
            Exception exception = Record.Exception(() => spell.Update(0.1d));

            Assert.Null(exception);
            Assert.Equal(15f, context.Values[Vital.Resource1]);
            ServerCombatLog packet = Assert.Single(context.VisiblePackets.OfType<ServerCombatLog>());
            Assert.Equal(5f, Assert.IsType<CombatLogVitalModifier>(packet.CombatLog).Amount);
            Assert.Single(context.VisiblePackets.OfType<Server07F8>());
        }

        [Fact]
        public void ImmediateEffect_ZeroEligibleTargetsDoesNotConsumeCost()
        {
            using var context = new EffectExecutionContext(100f);
            context.SetTargets();
            Spell4EffectsEntry effect = CreateEffect(1u, 10u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Empty(context.Mutations);
            Assert.Empty(context.Invocations);
        }

        [Fact]
        public void DelayedEffect_CancellationBeforeDueDoesNotConsumeCost()
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateEffect(1u, 10u, delayTime: 100u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();
            spell.Finish();
            spell.Update(1d);

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Empty(context.Mutations);
            Assert.Empty(context.Invocations);
        }

        [Fact]
        public void DelayedActivation_PreMutationReadExceptionRollsBackAndCancels()
        {
            using var context = new EffectExecutionContext(100f, 100f);
            context.ThrowOnRead = (vital, attempt) =>
                vital == Vital.Focus && attempt == 2;
            Spell4EffectsEntry first = CreateEffect(
                1u,
                25u,
                delayTime: 100u);
            Spell4EffectsEntry second = CreateEffect(
                2u,
                20u,
                delayTime: 100u,
                costVital: Vital.Focus);
            TestEffectSpell spell = context.CreateSpell([first, second]);

            spell.ExecuteForTest();
            context.SessionPackets.Clear();
            spell.Update(0.1d);

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Equal(100f, context.Values[Vital.Focus]);
            Assert.Equal(
                [(Vital.Resource1, -25f), (Vital.Resource1, 25f)],
                context.Mutations);
            Assert.Empty(context.Invocations);
            Assert.True(spell.IsFinishing);
            AssertVitalFailurePackets(context.SessionPackets, spell, CastResult.SpellBad);
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void DelayedActivation_ReconciliationReadExceptionRollsBackAndCancels()
        {
            using var context = new EffectExecutionContext(100f, 100f);
            context.ThrowBeforeMutation = (vital, delta) =>
                vital == Vital.Focus && delta < 0f;
            context.ThrowOnRead = (vital, attempt) =>
                vital == Vital.Focus && attempt == 3;
            Spell4EffectsEntry first = CreateEffect(
                1u,
                25u,
                delayTime: 100u);
            Spell4EffectsEntry second = CreateEffect(
                2u,
                20u,
                delayTime: 100u,
                costVital: Vital.Focus);
            TestEffectSpell spell = context.CreateSpell([first, second]);

            spell.ExecuteForTest();
            context.SessionPackets.Clear();
            spell.Update(0.1d);

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Equal(100f, context.Values[Vital.Focus]);
            Assert.Equal(
                [(Vital.Resource1, -25f), (Vital.Resource1, 25f)],
                context.Mutations);
            Assert.Empty(context.Invocations);
            Assert.True(spell.IsFinishing);
            AssertVitalFailurePackets(context.SessionPackets, spell, CastResult.SpellBad);
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void EffectWithoutHandler_DoesNotConsumeCost()
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateEffect(1u, 10u);
            effect.EffectType = unchecked((SpellEffectType)uint.MaxValue);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Empty(context.Mutations);
            Assert.Empty(context.Invocations);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ThrowingOrDroppedHandler_ConsumesCostOnce(bool throwFromHandler)
        {
            using var context = new EffectExecutionContext(100f);
            context.Handler = (_, _, effect) =>
            {
                if (throwFromHandler)
                    throw new InvalidOperationException("Test effect handler failure.");

                effect.DropEffect = true;
            };
            Spell4EffectsEntry effect = CreateEffect(1u, 10u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();

            Assert.Equal(90f, context.Values[Vital.Resource1]);
            Assert.Equal([(Vital.Resource1, -10f)], context.Mutations);
            Assert.Single(context.Invocations);
        }

        [Fact]
        public void NpcEffect_DoesNotValidateOrMutatePlayerOnlyCost()
        {
            using var context = new EffectExecutionContext(100f);
            Mock<IUnitEntity> npc = context.CreateTarget(9u);
            context.SetTargets(npc.Object);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                10u,
                costVital: Vital.PublicResource1);
            TestEffectSpell spell = context.CreateSpell([effect], npc.Object);

            spell.ExecuteForTest();

            Assert.Single(context.Invocations);
            npc.Verify(entity => entity.TryModifyVital(
                It.IsAny<Vital>(), It.IsAny<float>(), It.IsAny<IUnitEntity>()), Times.Never);
        }

        [Fact]
        public void ApplyPrerequisiteGate_PrecedesEffectCost()
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateEffect(1u, 10u);
            effect.PrerequisiteIdCasterApply = 99u;
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();
            spell.Update(1d);

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Empty(context.Mutations);
            Assert.Empty(context.Invocations);
        }

        [Fact]
        public void PhaseGate_PrecedesEffectCost()
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateEffect(1u, 10u);
            effect.PhaseFlags = 1u << 2;
            TestEffectSpell spell = context.CreateSpell(
                [effect],
                castMethod: CastMethod.Multiphase);
            spell.SetPhase((byte)1u);

            spell.ExecuteForTest();

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Empty(context.Mutations);
            Assert.Empty(context.Invocations);
        }

        [Fact]
        public void ActivationBatch_LaterInsufficientRowPreventsEveryHandler()
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry noCost = CreateEffect(1u, 0u);
            Spell4EffectsEntry insufficient = CreateEffect(2u, 101u);
            TestEffectSpell spell = context.CreateSpell([noCost, insufficient]);

            spell.ExecuteForTest();

            Assert.Empty(context.Invocations);
            Assert.Empty(context.Mutations);
            Assert.True(spell.IsFinishing);
            AssertVitalFailurePackets(
                context.SessionPackets,
                spell,
                CastResult.CasterVitalCostResource1);
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void PeriodicEffect_LaterInsufficientTickCancelsRemainingTimeline()
        {
            using var context = new EffectExecutionContext(15f);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                10u,
                tickTime: 100u,
                durationTime: 300u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();
            spell.Update(0.3d);

            Assert.Equal(5f, context.Values[Vital.Resource1]);
            Assert.Equal([(Vital.Resource1, -10f)], context.Mutations);
            Assert.Single(context.Invocations);
            Assert.True(spell.IsFinishing);
            AssertVitalFailurePackets(
                context.SessionPackets,
                spell,
                CastResult.CasterVitalCostResource1);
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void UnsupportedEffectCostFailsBeforeHandler()
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateEffect(
                1u,
                10u,
                costVital: Vital.PublicResource1);
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();

            Assert.Empty(context.Invocations);
            Assert.Empty(context.Mutations);
            Assert.True(spell.IsFinishing);
            AssertVitalFailurePackets(context.SessionPackets, spell, CastResult.SpellBad);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void EffectCostFailure_PacketExceptionIsContainedAndCleanupCompletes(
            int throwingCall)
        {
            using var context = new EffectExecutionContext(10f);
            int packetAttempts = 0;
            context.Session.Setup(session => session.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(_ =>
                {
                    packetAttempts++;
                    if (packetAttempts == throwingCall)
                        throw new InvalidOperationException("Test packet failure.");
                });
            Spell4EffectsEntry effect = CreateEffect(1u, 11u);
            TestEffectSpell spell = context.CreateSpell([effect]);

            Exception exception = Record.Exception(spell.ExecuteForTest);

            Assert.Null(exception);
            Assert.Equal(2, packetAttempts);
            Assert.Empty(context.Invocations);
            Assert.True(spell.IsFinishing);
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void ModifySpellCooldown_ExactImmediateRowExecutesAndPublishesOnce()
        {
            using var context = new EffectExecutionContext(100f);
            context.SpellManager
                .Setup(manager => manager.TryResetSpellCooldownsByBaseSpell(20684u))
                .Returns(true);
            Spell4EffectsEntry effect = CreateModifySpellCooldownEffect();
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();

            context.SpellManager.Verify(
                manager => manager.TryResetSpellCooldownsByBaseSpell(20684u),
                Times.Once);
            var invocation = Assert.Single(context.Invocations);
            Assert.Same(context.Player.Object, invocation.Target);
            Assert.Same(effect, invocation.Effect.Entry);
            Assert.False(invocation.Effect.DropEffect);
            Assert.Empty(invocation.Effect.CombatLogs);
            Assert.Equal(2, context.BaseAuthorityReads);
            Assert.Equal(2u, context.NextEffectIdValue);
            TargetInfo.EffectInfo published = Assert.Single(
                Assert.Single(Assert.Single(
                    context.VisiblePackets.OfType<ServerSpellGo>()).TargetInfoData).EffectInfoData);
            Assert.Equal(effect.Id, published.Spell4EffectId);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ModifySpellCooldown_UnsupportedImmediateOrDelayedRowNeverPassesInitialRegistration(
            bool delayed)
        {
            using var context = new EffectExecutionContext(100f);
            Spell4EffectsEntry effect = CreateModifySpellCooldownEffect();
            if (delayed)
                effect.DelayTime = 100u;
            else
                effect.DataBits02 = 3u;
            effect.InnateCostPerTickType0 = (uint)Vital.Resource1;
            effect.InnateCostPerTick0 = 10u;
            TestEffectSpell spell = context.CreateSpell([effect]);

            spell.ExecuteForTest();
            spell.Update(1d);
            spell.LateUpdate(0d);

            Assert.Equal(100f, context.Values[Vital.Resource1]);
            Assert.Empty(context.Mutations);
            Assert.Empty(context.Invocations);
            context.SpellManager.Verify(
                manager => manager.TryResetSpellCooldownsByBaseSpell(It.IsAny<uint>()),
                Times.Never);
            Assert.Empty(Assert.Single(
                context.VisiblePackets.OfType<ServerSpellGo>()).TargetInfoData);
            Assert.Empty(context.VisiblePackets.OfType<Server07F8>());
            Assert.Equal(0, context.BaseAuthorityReads);
            Assert.Equal(1u, context.NextEffectIdValue);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void ModifySpellCooldown_ResetFailureDropsOnlyThatEffectAndPreservesOrder()
        {
            using var context = new EffectExecutionContext(100f);
            var lifecycle = new List<string>();
            context.SpellManager
                .Setup(manager => manager.TryResetSpellCooldownsByBaseSpell(20684u))
                .Callback(() => lifecycle.Add("reset"))
                .Returns(false);
            context.Handler = (_, _, _) => lifecycle.Add("damage");
            Spell4EffectsEntry reset = CreateModifySpellCooldownEffect();
            Spell4EffectsEntry damage = CreateEffect(83529u, 0u);
            damage.OrderIndex = 1u;
            TestEffectSpell spell = context.CreateSpell([reset, damage]);

            spell.ExecuteForTest();

            Assert.Equal(["reset", "damage"], lifecycle);
            Assert.Collection(
                context.Invocations,
                invocation => Assert.True(invocation.Effect.DropEffect),
                invocation => Assert.False(invocation.Effect.DropEffect));
            TargetInfo targetInfo = Assert.Single(
                Assert.Single(context.VisiblePackets.OfType<ServerSpellGo>()).TargetInfoData);
            TargetInfo.EffectInfo published = Assert.Single(targetInfo.EffectInfoData);
            Assert.Equal(damage.Id, published.Spell4EffectId);
        }

        private static Spell4EffectsEntry CreateEffect(
            uint id,
            uint cost,
            uint delayTime = 0u,
            uint tickTime = 0u,
            uint durationTime = 0u,
            Vital costVital = Vital.Resource1)
        {
            return new Spell4EffectsEntry
            {
                Id                         = id,
                EffectType                 = SpellEffectType.UNUSED039,
                TargetFlags                = (uint)SpellEffectTargetFlags.Caster,
                PhaseFlags                 = 1u,
                DelayTime                  = delayTime,
                TickTime                   = tickTime,
                DurationTime               = durationTime,
                InnateCostPerTickType0     = (uint)costVital,
                InnateCostPerTick0         = cost
            };
        }

        private static Spell4EffectsEntry CreateVitalModifierEffect(
            uint id,
            Vital vital,
            int amount,
            uint delayTime = 0u,
            uint tickTime = 0u,
            uint durationTime = 0u)
        {
            uint rawAmount = unchecked((uint)amount);
            return new Spell4EffectsEntry
            {
                Id             = id,
                SpellId        = 123u,
                EffectType     = SpellEffectType.VitalModifier,
                TargetFlags    = (uint)SpellEffectTargetFlags.Caster,
                PhaseFlags     = 1u,
                DelayTime      = delayTime,
                TickTime       = tickTime,
                DurationTime   = durationTime,
                DataBits00     = (uint)vital,
                DataBits01     = rawAmount,
                DataBits02     = rawAmount,
                DataBits09     = 1u,
                ParameterType  = new SpellEffectParameterType[4],
                ParameterValue = new float[4]
            };
        }

        private static Spell4EffectsEntry CreateModifySpellCooldownEffect()
        {
            return SpellEffectSupportPolicyTests.ExactEntry();
        }

        private static void AssertVitalFailurePackets(
            IReadOnlyList<IWritable> packets,
            ISpell spell,
            CastResult expected)
        {
            Assert.Collection(
                packets,
                packet =>
                {
                    ServerSpellCastResult result = Assert.IsType<ServerSpellCastResult>(packet);
                    Assert.Equal(spell.Parameters.SpellInfo.Entry.Id, result.Spell4Id);
                    Assert.Equal(expected, result.CastResult);
                },
                packet =>
                {
                    Server07F9 cancel = Assert.IsType<Server07F9>(packet);
                    Assert.Equal(spell.CastingId, cancel.ServerUniqueId);
                    Assert.Equal(expected, cancel.CastResult);
                    Assert.True(cancel.CancelCast);
                });
        }

        private sealed class EffectExecutionContext : IDisposable
        {
            public Mock<IPlayer> Player { get; } = new();
            public Mock<IGameSession> Session { get; } = new();
            public Mock<IBaseMap> Map { get; } = new();
            public Mock<ISpellManager> SpellManager { get; } = new();
            public int BaseAuthorityReads { get; private set; }
            public uint NextEffectIdValue => (uint)typeof(GlobalSpellManager).GetField(
                "nextEffectId",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(globalSpellManager);

            public Dictionary<Vital, float> Values { get; } = [];
            public Dictionary<Vital, float> Maxima { get; } = [];
            public List<(Vital Vital, float Delta)> Mutations { get; } = [];
            public List<(ISpell Spell, IUnitEntity Target, ISpellTargetEffectInfo Effect)> Invocations { get; } = [];
            public List<IWritable> SessionPackets { get; } = [];
            public List<IWritable> VisiblePackets { get; } = [];
            public Action<ISpell, IUnitEntity, ISpellTargetEffectInfo> Handler { get; set; }
            public Func<Vital, int, bool> ThrowOnRead { get; set; }
            public Func<Vital, float, bool> ThrowBeforeMutation { get; set; }
            public Func<Vital, float, bool> ThrowAfterMutation { get; set; }

            private readonly List<IUnitEntity> selectedTargets = [];
            private readonly Dictionary<Vital, int> vitalReadAttempts = [];
            private readonly IServiceProvider previousProvider;
            private readonly ServiceProvider serviceProvider;
            private readonly GlobalSpellManager globalSpellManager;

            public EffectExecutionContext(float resource1, float? focus = null)
            {
                Values[Vital.Resource1] = resource1;
                Maxima[Vital.Resource1] = 100f;
                if (focus.HasValue)
                {
                    Values[Vital.Focus] = focus.Value;
                    Maxima[Vital.Focus] = 100f;
                }

                previousProvider = LegacyServiceProvider.Provider;

                var scriptCollection = new Mock<IScriptCollection>();
                var scriptManager = new Mock<IScriptManager>();
                scriptManager.Setup(manager => manager.InitialiseOwnedScripts<ISpell>(
                        It.IsAny<ISpell>(), It.IsAny<uint>()))
                    .Returns(scriptCollection.Object);

                globalSpellManager = new GlobalSpellManager(_ =>
                {
                    BaseAuthorityReads++;
                    return true;
                });
                FieldInfo handlerField = typeof(GlobalSpellManager).GetField(
                    "spellEffectDelegates",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var handlers = (Dictionary<SpellEffectType, SpellEffectDelegate>)handlerField.GetValue(globalSpellManager);
                handlers.Add(SpellEffectType.UNUSED039, (spell, target, effect) =>
                {
                    Invocations.Add((spell, target, effect));
                    Handler?.Invoke(spell, target, effect);
                });
                handlers.Add(SpellEffectType.VitalModifier, SpellHandler.HandleEffectVitalModifier);
                handlers.Add(SpellEffectType.ModifySpellCooldown, (spell, target, effect) =>
                {
                    Invocations.Add((spell, target, effect));
                    SpellHandler.HandleEffectModifySpellCooldown(spell, target, effect);
                });

                serviceProvider = new ServiceCollection()
                    .AddSingleton(scriptManager.Object)
                    .AddSingleton(globalSpellManager)
                    .BuildServiceProvider();
                LegacyServiceProvider.Provider = serviceProvider;

                Player.SetupGet(entity => entity.Guid).Returns(7u);
                Player.SetupGet(entity => entity.InWorld).Returns(true);
                Player.SetupGet(entity => entity.Map).Returns(Map.Object);
                Player.SetupGet(entity => entity.IsLoading).Returns(false);
                Player.SetupGet(entity => entity.Session).Returns(Session.Object);
                Player.SetupGet(entity => entity.SpellManager).Returns(SpellManager.Object);
                Player.Setup(entity => entity.TryGetVitalValue(
                        It.IsAny<Vital>(),
                        out It.Ref<float>.IsAny))
                    .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                    {
                        Vital canonical = Canonical(vital);
                        int attempt = vitalReadAttempts.GetValueOrDefault(canonical) + 1;
                        vitalReadAttempts[canonical] = attempt;
                        if (ThrowOnRead?.Invoke(canonical, attempt) == true)
                            throw new InvalidOperationException("Test vital read failure.");

                        return Values.TryGetValue(canonical, out value);
                    }));
                Player.Setup(entity => entity.TryGetVitalMaximum(
                        It.IsAny<Vital>(),
                        out It.Ref<float>.IsAny))
                    .Returns(new TryGetVitalMaximum((Vital vital, out float maximum) =>
                        Maxima.TryGetValue(Canonical(vital), out maximum)));
                Player.Setup(entity => entity.TryModifyVital(
                        It.IsAny<Vital>(),
                        It.IsAny<float>(),
                        It.IsAny<IUnitEntity>()))
                    .Returns((Vital vital, float delta, IUnitEntity _) =>
                    {
                        Vital canonical = Canonical(vital);
                        if (ThrowBeforeMutation?.Invoke(canonical, delta) == true)
                            throw new InvalidOperationException("Test pre-mutation failure.");

                        if (!Values.TryGetValue(canonical, out float current))
                            return false;

                        Mutations.Add((vital, delta));
                        Values[canonical] = current + delta;
                        if (ThrowAfterMutation?.Invoke(canonical, delta) == true)
                            throw new InvalidOperationException("Test post-mutation notification failure.");

                        return true;
                    });
                Player.Setup(entity => entity.EnqueueToVisible(
                        It.IsAny<IWritable>(),
                        It.IsAny<bool>()))
                    .Callback<IWritable, bool>((packet, _) => VisiblePackets.Add(packet));
                Session.Setup(session => session.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                    .Callback<IWritable>(SessionPackets.Add);
                selectedTargets.Add(Player.Object);
            }

            public Mock<IUnitEntity> CreateTarget(uint guid)
            {
                var target = new Mock<IUnitEntity>();
                target.SetupGet(entity => entity.Guid).Returns(guid);
                target.SetupGet(entity => entity.InWorld).Returns(true);
                target.SetupGet(entity => entity.Map).Returns(Map.Object);
                return target;
            }

            public void SetTargets(params IUnitEntity[] targets)
            {
                selectedTargets.Clear();
                selectedTargets.AddRange(targets);
            }

            public TestEffectSpell CreateSpell(
                List<Spell4EffectsEntry> effects,
                IUnitEntity caster = null,
                CastMethod castMethod = CastMethod.Normal)
            {
                var baseInfo = new Mock<ISpellBaseInfo>();
                baseInfo.SetupGet(info => info.Entry).Returns(new Spell4BaseEntry
                {
                    CastMethod = (uint)castMethod
                });
                var spellInfo = new Mock<ISpellInfo>();
                spellInfo.SetupGet(info => info.Entry).Returns(new Spell4Entry { Id = 123u });
                spellInfo.SetupGet(info => info.BaseInfo).Returns(baseInfo.Object);
                spellInfo.SetupGet(info => info.Effects).Returns(effects);
                spellInfo.SetupGet(info => info.Telegraphs).Returns([]);
                spellInfo.SetupGet(info => info.PrerequisiteRunners).Returns([]);
                var parameters = new SpellParameters
                {
                    SpellInfo = spellInfo.Object
                };
                var spell = new TestEffectSpell(
                    caster ?? Player.Object,
                    parameters,
                    () => selectedTargets);
                spell.SetStatus(SpellStatus.Casting);
                return spell;
            }

            public void Dispose()
            {
                LegacyServiceProvider.Provider = previousProvider;
                serviceProvider.Dispose();
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

        private sealed class TestEffectSpell : NexusForever.Game.Spell.Spell
        {
            private readonly Func<IReadOnlyList<IUnitEntity>> targetProvider;

            public TestEffectSpell(
                IUnitEntity caster,
                ISpellParameters parameters,
                Func<IReadOnlyList<IUnitEntity>> targetProvider)
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

            public void SetPhase(byte phase)
            {
                currentPhase = phase;
            }

            protected override void SelectTargets()
            {
                foreach (IUnitEntity target in targetProvider())
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Caster, target));
            }
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
        private delegate bool TryGetVitalMaximum(Vital vital, out float maximum);
    }
}
