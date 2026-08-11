using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Event;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Combat;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Network.Session;
using NexusForever.Script;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    [Collection(CombatServiceProviderCollection.Name)]
    public class SpellBaseTests
    {
        [Fact]
        public void ISpell_HasLateUpdateMethod()
        {
            // Verify the interface exposes LateUpdate
            var type = typeof(ISpell);
            var method = type.GetMethod("LateUpdate");
            Assert.NotNull(method);
            Assert.Equal(typeof(void), method.ReturnType);
        }

        [Fact]
        public void ISpell_HasFinishMethod()
        {
            var type = typeof(ISpell);
            var method = type.GetMethod("Finish");
            Assert.NotNull(method);
        }

        [Fact]
        public void ISpell_HasIsFinishingProperty()
        {
            var type = typeof(ISpell);
            var prop = type.GetProperty("IsFinishing");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop.PropertyType);
        }

        [Fact]
        public void ISpell_HasIsWaitingProperty()
        {
            var type = typeof(ISpell);
            var prop = type.GetProperty("IsWaiting");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop.PropertyType);
        }

        [Fact]
        public void Spell_CastMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("Cast");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_CancelCastMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("CancelCast");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_UpdateMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("Update");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_LateUpdateMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("LateUpdate");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_FinishMethodIsVirtual()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod("Finish");
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
        }

        [Fact]
        public void Spell_HasProtectedCanFinishMethod()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod(
                "CanFinish",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
            Assert.True(method.IsFamily); // protected
        }

        [Fact]
        public void Spell_HasProtectedVirtualExecute()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod(
                "Execute",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
            Assert.True(method.IsFamily); // protected
        }

        [Fact]
        public void Spell_HasProtectedVirtualSelectTargets()
        {
            var method = typeof(NexusForever.Game.Spell.Spell).GetMethod(
                "SelectTargets",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
            Assert.True(method.IsFamily); // protected
        }

        [Fact]
        public void Spell_StatusFieldIsProtected()
        {
            var field = typeof(NexusForever.Game.Spell.Spell).GetField(
                "status",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field);
            Assert.True(field.IsFamily); // protected
        }

        [Fact]
        public void DelayedPeriodicEffects_UseFreshSnapshotsAndHoldSpellUntilRelativeExpiry()
        {
            using var context = new SpellTimelineTestContext();
            var effect = SpellTimelineTestContext.CreateEffect(
                1u,
                delayTime: 500u,
                tickTime: 100u,
                durationTime: 300u);
            var spell = new TestTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Normal,
                    new Spell4Entry { Id = 123u },
                    [effect]));
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            spell.LateUpdate(0d);

            Assert.Empty(context.Invocations);
            Assert.False(spell.IsFinished);
            ServerSpellGo initialGo = Assert.Single(context.Packets.OfType<ServerSpellGo>());
            TargetInfo.EffectInfo advertisedEffect = Assert.Single(
                Assert.Single(initialGo.TargetInfoData).EffectInfoData);
            Assert.Equal(500u, advertisedEffect.DelayTime);
            Assert.Equal(300, advertisedEffect.TimeRemaining);

            spell.Update(0.799d);
            Assert.Equal(2, context.Invocations.Count);
            spell.LateUpdate(0d);
            Assert.False(spell.IsFinished);

            spell.Update(0.001d);
            Assert.Equal(3, context.Invocations.Count);
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);

            Server07F8[] activationPackets = context.Packets.OfType<Server07F8>().ToArray();
            Assert.Equal(3, activationPackets.Length);
            Assert.All(activationPackets, packet =>
            {
                Assert.Equal(spell.CastingId, packet.CastingId);
                Assert.Equal(effect.Id, packet.Spell4EffectId);
                Assert.Equal(context.Caster.Object.Guid, packet.TargetId);
            });
        }

        [Fact]
        public void DelayedActivation_UsesImmutableRegistrationTargetSnapshot()
        {
            using var context = new SpellTimelineTestContext();
            var firstTarget = new Mock<IUnitEntity>();
            firstTarget.SetupGet(entity => entity.Guid).Returns(10u);
            firstTarget.SetupGet(entity => entity.InWorld).Returns(true);
            firstTarget.SetupGet(entity => entity.Map).Returns(context.Map.Object);
            var replacementTarget = new Mock<IUnitEntity>();
            replacementTarget.SetupGet(entity => entity.Guid).Returns(11u);
            IWorldEntity visibleTarget = firstTarget.Object;
            context.Caster.Setup(entity => entity.GetVisible<IWorldEntity>(42u))
                .Returns(() => visibleTarget);
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u, delayTime: 100u);
            effect.TargetFlags = (uint)SpellEffectTargetFlags.Target;
            SpellParameters parameters = SpellTimelineTestContext.CreateParameters(
                CastMethod.Normal,
                new Spell4Entry { Id = 123u },
                [effect]);
            parameters.PrimaryTargetId = 42u;
            var spell = new TestTimelineSpell(context.Caster.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            visibleTarget = replacementTarget.Object;
            spell.Update(0.1d);

            Assert.Same(firstTarget.Object, Assert.Single(context.Invocations).Target);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DelayedActivation_SkipsDespawnedOrCrossMapCapturedReference(bool crossMap)
        {
            using var context = new SpellTimelineTestContext();
            bool inWorld = true;
            IBaseMap targetMap = context.Map.Object;
            var capturedTarget = new Mock<IUnitEntity>();
            capturedTarget.SetupGet(entity => entity.Guid).Returns(10u);
            capturedTarget.SetupGet(entity => entity.InWorld).Returns(() => inWorld);
            capturedTarget.SetupGet(entity => entity.Map).Returns(() => targetMap);
            var replacementTarget = new Mock<IUnitEntity>();
            replacementTarget.SetupGet(entity => entity.Guid).Returns(10u);
            replacementTarget.SetupGet(entity => entity.InWorld).Returns(true);
            replacementTarget.SetupGet(entity => entity.Map).Returns(context.Map.Object);
            IWorldEntity visibleTarget = capturedTarget.Object;
            context.Caster.Setup(entity => entity.GetVisible<IWorldEntity>(42u))
                .Returns(() => visibleTarget);
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u, delayTime: 100u);
            effect.TargetFlags = (uint)SpellEffectTargetFlags.Target;
            SpellParameters parameters = SpellTimelineTestContext.CreateParameters(
                CastMethod.Normal,
                new Spell4Entry { Id = 123u },
                [effect]);
            parameters.PrimaryTargetId = 42u;
            var spell = new TestTimelineSpell(context.Caster.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            visibleTarget = replacementTarget.Object;
            if (crossMap)
                targetMap = Mock.Of<IBaseMap>();
            else
                inWorld = false;
            spell.Update(0.1d);

            Assert.Empty(context.Invocations);
        }

        [Fact]
        public void EffectActivationFailure_IsolatedAndDoesNotBlockLaterRow()
        {
            using var context = new SpellTimelineTestContext
            {
                Handler = (_, _, info) =>
                {
                    if (info.Entry.Id == 1u)
                        throw new InvalidOperationException("Test effect failure.");
                }
            };
            var failingEffect = SpellTimelineTestContext.CreateEffect(
                1u,
                tickTime: 100u,
                durationTime: 500u);
            var succeedingEffect = SpellTimelineTestContext.CreateEffect(
                2u,
                tickTime: 100u,
                durationTime: 500u);
            var spell = new TestTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Normal,
                    new Spell4Entry { Id = 123u },
                    [failingEffect, succeedingEffect]));
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            spell.Update(0.1d);

            Assert.False(spell.IsFinishing);
            Assert.Equal(2, context.InvocationAttempts);
            Assert.Equal(2u, Assert.Single(context.Invocations).Effect.Entry.Id);
            Assert.Equal(2u, Assert.Single(context.Packets.OfType<Server07F8>()).Spell4EffectId);

            spell.Finish();
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void LaterActivation_CombatLogFailureDoesNotSuppressFollowUpPacket()
        {
            using var context = new SpellTimelineTestContext();
            var combatLog = new Mock<ICombatLog>();
            context.Handler = (_, _, info) => info.AddCombatLog(combatLog.Object);
            var attemptedPackets = new List<IWritable>();
            context.Caster.Setup(entity => entity.EnqueueToVisible(It.IsAny<IWritable>(), true))
                .Callback<IWritable, bool>((packet, _) =>
                {
                    attemptedPackets.Add(packet);
                    if (packet is ServerCombatLog)
                        throw new InvalidOperationException("Test combat-log packet failure.");
                });
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u, delayTime: 100u);
            var spell = new TestTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Normal,
                    new Spell4Entry { Id = 123u },
                    [effect]));
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            Exception exception = Record.Exception(() => spell.Update(0.1d));

            Assert.Null(exception);
            Assert.Collection(
                attemptedPackets,
                packet => Assert.IsType<ServerSpellGo>(packet),
                packet => Assert.Same(combatLog.Object, Assert.IsType<ServerCombatLog>(packet).CombatLog),
                packet => Assert.IsType<Server07F8>(packet));
        }

        [Fact]
        public void DelayedDroppedEffect_PublishesDeflectLogWithoutFollowUpPacket()
        {
            using var context = new SpellTimelineTestContext();
            var combatLog = new CombatLogDeflect
            {
                BMultiHit = false,
                CastData  = new CombatLogCastData()
            };
            context.Handler = (_, _, info) =>
            {
                info.DropEffect = true;
                info.AddCombatLog(combatLog);
            };
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u, delayTime: 100u);
            var spell = new TestTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Normal,
                    new Spell4Entry { Id = 123u },
                    [effect]));
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            spell.Update(0.1d);

            ServerCombatLog packet = Assert.Single(context.Packets.OfType<ServerCombatLog>());
            Assert.Same(combatLog, packet.CombatLog);
            Assert.Empty(context.Packets.OfType<Server07F8>());
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 3)]
        public void DelayedOrTickingProxy_InvokesHandlerWithoutFollowUpPacket(
            bool ticking,
            int expectedInvocations)
        {
            using var context = new SpellTimelineTestContext();
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(
                1u,
                delayTime: ticking ? 0u : 100u,
                tickTime: ticking ? 100u : 0u,
                durationTime: ticking ? 300u : 0u);
            effect.EffectType = SpellEffectType.Proxy;
            var spell = new TestTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Normal,
                    new Spell4Entry { Id = 123u },
                    [effect]));
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            spell.Update(ticking ? 0.3d : 0.1d);

            Assert.Equal(expectedInvocations, context.InvocationAttempts);
            Assert.Empty(context.Packets.OfType<Server07F8>());
        }

        [Fact]
        public void InitialCombatLogFailure_DoesNotSuppressSpellGoAttempt()
        {
            using var context = new SpellTimelineTestContext();
            var combatLog = new Mock<ICombatLog>();
            context.Handler = (_, _, info) => info.AddCombatLog(combatLog.Object);
            var attemptedPackets = new List<IWritable>();
            context.Caster.Setup(entity => entity.EnqueueToVisible(It.IsAny<IWritable>(), true))
                .Callback<IWritable, bool>((packet, _) =>
                {
                    attemptedPackets.Add(packet);
                    if (packet is ServerCombatLog)
                        throw new InvalidOperationException("Test initial combat-log packet failure.");
                });
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(1u);
            var spell = new TestTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Normal,
                    new Spell4Entry { Id = 123u },
                    [effect]));
            spell.SetStatus(SpellStatus.Casting);

            Exception exception = Record.Exception(() => spell.ExecuteForTest());

            Assert.Null(exception);
            Assert.Collection(
                attemptedPackets,
                packet => Assert.Same(combatLog.Object, Assert.IsType<ServerCombatLog>(packet).CombatLog),
                packet => Assert.IsType<ServerSpellGo>(packet));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void DelayedOrTickingApplyPrerequisite_FailsClosedWithoutInvocation(
            bool ticking,
            bool targetPrerequisite)
        {
            using var context = new SpellTimelineTestContext();
            Spell4EffectsEntry effect = SpellTimelineTestContext.CreateEffect(
                1u,
                delayTime: ticking ? 0u : 100u,
                tickTime: ticking ? 100u : 0u,
                durationTime: ticking ? 300u : 0u);
            if (targetPrerequisite)
                effect.PrerequisiteIdTargetApply = 99u;
            else
                effect.PrerequisiteIdCasterApply = 99u;
            var spell = new TestTimelineSpell(
                context.Caster.Object,
                SpellTimelineTestContext.CreateParameters(
                    CastMethod.Normal,
                    new Spell4Entry { Id = 123u },
                    [effect]));
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            spell.Update(1d);
            spell.LateUpdate(0d);

            Assert.Empty(context.Invocations);
            Assert.Empty(context.Packets.OfType<Server07F8>());
            Assert.Empty(Assert.Single(context.Packets.OfType<ServerSpellGo>()).TargetInfoData);
            Assert.True(spell.IsFinished);
        }
    }

    internal sealed class SpellTimelineTestContext : IDisposable
    {
        public Mock<IUnitEntity> Caster { get; } = new();
        public Mock<IBaseMap> Map { get; } = new();
        public List<(ISpell Spell, IUnitEntity Target, ISpellTargetEffectInfo Effect)> Invocations { get; } = [];
        public List<IWritable> Packets { get; } = [];
        public Action<ISpell, IUnitEntity, ISpellTargetEffectInfo> Handler { get; set; }
        public int InvocationAttempts { get; private set; }

        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public SpellTimelineTestContext()
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
            SpellEffectDelegate handler = (spell, target, effect) =>
            {
                InvocationAttempts++;
                Handler?.Invoke(spell, target, effect);
                Invocations.Add((spell, target, effect));
            };
            handlers.Add(SpellEffectType.UNUSED039, handler);
            handlers.Add(SpellEffectType.Proxy, handler);

            serviceProvider = new ServiceCollection()
                .AddSingleton(scriptManager.Object)
                .AddSingleton(globalSpellManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;

            Caster.SetupGet(entity => entity.Guid).Returns(7u);
            Caster.SetupGet(entity => entity.InWorld).Returns(true);
            Caster.SetupGet(entity => entity.Map).Returns(Map.Object);
            Caster.Setup(entity => entity.EnqueueToVisible(It.IsAny<IWritable>(), It.IsAny<bool>()))
                .Callback<IWritable, bool>((packet, _) => Packets.Add(packet));
        }

        public static Spell4EffectsEntry CreateEffect(
            uint id,
            uint delayTime = 0u,
            uint tickTime = 0u,
            uint durationTime = 0u,
            uint phaseFlags = 1u)
        {
            return new Spell4EffectsEntry
            {
                Id           = id,
                EffectType   = SpellEffectType.UNUSED039,
                TargetFlags  = (uint)SpellEffectTargetFlags.Caster,
                DelayTime    = delayTime,
                TickTime     = tickTime,
                DurationTime = durationTime,
                PhaseFlags   = phaseFlags
            };
        }

        public static SpellParameters CreateParameters(
            CastMethod castMethod,
            Spell4Entry entry,
            List<Spell4EffectsEntry> effects)
        {
            var baseInfo = new Mock<ISpellBaseInfo>();
            baseInfo.SetupGet(info => info.Entry).Returns(new Spell4BaseEntry
            {
                CastMethod = (uint)castMethod
            });
            var spellInfo = new Mock<ISpellInfo>();
            spellInfo.SetupGet(info => info.Entry).Returns(entry);
            spellInfo.SetupGet(info => info.BaseInfo).Returns(baseInfo.Object);
            spellInfo.SetupGet(info => info.Effects).Returns(effects);
            spellInfo.SetupGet(info => info.Telegraphs).Returns([]);
            spellInfo.SetupGet(info => info.PrerequisiteRunners).Returns([]);
            return new SpellParameters
            {
                SpellInfo = spellInfo.Object
            };
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }
    }

    internal sealed class TestTimelineSpell : NexusForever.Game.Spell.Spell
    {
        public TestTimelineSpell(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        public void ExecuteForTest(bool consumeVitalCost = true)
        {
            Execute(consumeVitalCost);
        }

        public void SetStatus(SpellStatus value)
        {
            status = value;
        }
    }
}
