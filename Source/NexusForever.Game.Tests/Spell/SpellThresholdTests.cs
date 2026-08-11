using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Entity;
using NexusForever.Game.Spell;
using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Script;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Spell
{
    [Collection(CombatServiceProviderCollection.Name)]
    public sealed class SpellThresholdTests : IDisposable
    {
        private const uint RootSpellId = 100u;
        private const uint ChildSpellId = 200u;

        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public SpellThresholdTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var scriptCollection = new Mock<IScriptCollection>();
            var scriptManager = new Mock<IScriptManager>();
            scriptManager.Setup(manager => manager.InitialiseOwnedScripts<ISpell>(
                    It.IsAny<ISpell>(), It.IsAny<uint>()))
                .Returns(scriptCollection.Object);

            serviceProvider = new ServiceCollection()
                .AddSingleton(scriptManager.Object)
                .AddSingleton(new GlobalSpellManager())
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        [Fact]
        public void Spell4Thresholds_IsLoadedAndThresholdTypesRemainDiscoverable()
        {
            PropertyInfo property = typeof(GameTableManager).GetProperty(nameof(GameTableManager.Spell4Thresholds));

            Assert.NotNull(property?.GetCustomAttribute<GameDataAttribute>());
            AssertSpellType<SpellRapidTap>(CastMethod.RapidTap);
            AssertSpellType<SpellChargeRelease>(CastMethod.ChargeRelease);
        }

        [Fact]
        public void RapidTap_RepeatedChildRowsDispatchDistinctChildrenWithCapturedLineageAndPacketOrder()
        {
            var lifecycle = new List<object>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out IBaseMap map);
            var target = new Mock<IUnitEntity>();
            target.SetupGet(unit => unit.InWorld).Returns(true);
            target.SetupGet(unit => unit.Map).Returns(map);
            player.Setup(unit => unit.GetVisible<IWorldEntity>(42u)).Returns(target.Object);

            Position admittedPosition = new(new Vector3(1f, 2f, 3f));
            SpellParameters parameters = CreateRootParameters(
                CastMethod.RapidTap,
                thresholdTime: 1000u,
                primaryTargetId: 42u,
                position: admittedPosition,
                userInitiated: true,
                procTriggered: true);
            ISpellInfo admittedRoot = parameters.RootSpellInfo;
            ThresholdData data = CreateData(
                Row(2u, 1u, ChildSpellId, 100u),
                Row(1u, 0u, ChildSpellId, 100u));
            var children = new List<FakeSpell>();
            var spell = new TestRapidTap(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                lifecycle.Add("child");
                childParameters.SpellInfo = data.Spells[spell4Id];
                var child = new FakeSpell(player.Object, childParameters);
                children.Add(child);
                return child;
            });

            spell.Cast();
            parameters.PrimaryTargetId = 99u;
            parameters.Position = new Position(new Vector3(9f, 9f, 9f));
            parameters.RootSpellInfo = CreateSpellInfo(new Spell4Entry { Id = 999u }, CastMethod.Normal);
            spell.Update(0d);
            Assert.True(spell.TryHandleThresholdInput(true));
            Assert.True(spell.TryHandleThresholdInput(true));

            Assert.Equal(2, children.Count);
            Assert.NotSame(children[0], children[1]);
            Assert.All(children, child =>
            {
                Assert.Same(parameters.SpellInfo, child.Parameters.ParentSpellInfo);
                Assert.Same(admittedRoot, child.Parameters.RootSpellInfo);
                Assert.Same(spell, child.Parameters.ThresholdParent);
                Assert.Equal(42u, child.Parameters.PrimaryTargetId);
                Assert.Equal(admittedPosition.Vector, child.Parameters.Position.Vector);
                Assert.NotSame(admittedPosition, child.Parameters.Position);
                Assert.True(child.Parameters.UserInitiatedSpellCast);
                Assert.True(child.Parameters.IsProcTriggered);
                Assert.True(child.Parameters.IsThresholdChild);
                Assert.Null(child.Parameters.CharacterSpell);
                Assert.True(spell.IsValidThresholdChild(child));
            });
            Assert.Equal(new byte[] { 1, 2 }, children.Select(child => child.Parameters.ThresholdValue));

            var impostor = new FakeSpell(player.Object, children[0].Parameters);
            Assert.False(spell.IsValidThresholdChild(impostor));
            Assert.Collection(
                lifecycle,
                value =>
                {
                    Server0816 start = Assert.IsType<Server0816>(value);
                    Assert.Equal(RootSpellId, start.Spell4Id);
                    Assert.Equal(RootSpellId, start.RootSpell4Id);
                    Assert.Equal(0u, start.ParentSpell4Id);
                    Assert.Equal(spell.CastingId, start.CastingId);
                },
                value => Assert.Equal("child", value),
                value =>
                {
                    Server0817 update = Assert.IsType<Server0817>(value);
                    Assert.Equal(RootSpellId, update.Spell4Id);
                    Assert.Equal((byte)1, update.Unknown0);
                },
                value => Assert.Equal("child", value),
                value =>
                {
                    Server0817 update = Assert.IsType<Server0817>(value);
                    Assert.Equal(RootSpellId, update.Spell4Id);
                    Assert.Equal((byte)2, update.Unknown0);
                },
                value => Assert.Equal(RootSpellId, Assert.IsType<Server0814>(value).Spell4Id));

            spell.LateUpdate(0d);
            Assert.False(spell.IsFinished);
            children.ForEach(child => child.Finish());
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void RapidTap_InvalidDeltasMakeNoProgressAndHugeResidualClosesOnce()
        {
            var lifecycle = new List<object>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _);
            SpellParameters parameters = CreateRootParameters(
                CastMethod.RapidTap,
                thresholdTime: 500u,
                castTime: 100u);
            ThresholdData data = CreateData(Row(1u, 0u, ChildSpellId, 100u));
            var spell = new TestRapidTap(player.Object, parameters, data, (_, _) =>
                throw new InvalidOperationException("No child should start."));

            spell.Cast();
            spell.Update(-1d);
            spell.Update(double.NaN);
            spell.Update(double.PositiveInfinity);

            Assert.True(spell.IsCasting);
            Assert.Empty(lifecycle);

            spell.Update(0.1d);
            Assert.True(spell.IsWaiting);
            Assert.Single(lifecycle, value => value is Server0816);

            spell.Update(double.MaxValue);
            spell.Update(double.MaxValue);

            Assert.True(spell.IsFinishing);
            Assert.Single(lifecycle, value => value is Server0814);
            Assert.DoesNotContain(lifecycle, value => value is Server0817);
        }

        [Fact]
        public void RapidTap_ParentCostChargeCooldownAndGlobalCooldownCommitOnceAcrossAllTaps()
        {
            var lifecycle = new List<object>();
            var values = new Dictionary<Vital, float> { [Vital.Focus] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _, values, mutations);
            var characterSpell = new Mock<ICharacterSpell>();
            characterSpell.SetupGet(spell => spell.MaxAbilityCharges).Returns(1u);
            characterSpell.SetupGet(spell => spell.AbilityCharges).Returns(1u);
            SpellParameters parameters = CreateRootParameters(CastMethod.RapidTap, thresholdTime: 1000u);
            parameters.CharacterSpell = characterSpell.Object;
            parameters.SpellInfo.Entry.InnateCostType0 = (uint)Vital.Focus;
            parameters.SpellInfo.Entry.InnateCost0 = 10u;
            parameters.SpellInfo.Entry.SpellCoolDown = 2000u;
            Mock.Get(parameters.SpellInfo).SetupGet(info => info.GlobalCooldown)
                .Returns(new SpellCoolDownEntry { CooldownTime = 1500u });
            ThresholdData data = CreateData(
                Row(1u, 0u, ChildSpellId, 100u),
                Row(2u, 1u, ChildSpellId, 100u));
            var spell = new TestRapidTap(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                childParameters.SpellInfo = data.Spells[spell4Id];
                return new FakeSpell(player.Object, childParameters);
            });

            spell.Cast();
            spell.Update(0d);
            spell.TryHandleThresholdInput(true);
            spell.TryHandleThresholdInput(true);
            spell.TryHandleThresholdInput(true);

            Assert.Equal(90f, values[Vital.Focus]);
            Assert.Equal([(Vital.Focus, -10f)], mutations);
            characterSpell.Verify(character => character.UseCharge(), Times.Once);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetGlobalSpellCooldown(1.5d), Times.Once);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(RootSpellId, 2d), Times.Once);
        }

        [Fact]
        public void ThresholdChild_SuppressesOwnBaseTransactionButRetainsEffectCostAndUserMarker()
        {
            var lifecycle = new List<object>();
            var values = new Dictionary<Vital, float>
            {
                [Vital.Resource1] = 100f,
                [Vital.Focus] = 100f
            };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _, values, mutations);
            int effectCount = 0;
            RegisterEffectHandler(SpellEffectType.UNUSED039, (_, _, _) => effectCount++);
            var effect = new Spell4EffectsEntry
            {
                Id = 900u,
                SpellId = ChildSpellId,
                EffectType = SpellEffectType.UNUSED039,
                TargetFlags = (uint)SpellEffectTargetFlags.Caster,
                InnateCostPerTickType0 = (uint)Vital.Focus,
                InnateCostPerTick0 = 7u,
                PhaseFlags = 1u
            };
            SpellParameters parameters = CreateRootParameters(
                CastMethod.ChargeRelease,
                thresholdTime: 500u,
                userInitiated: true);
            ThresholdData data = CreateData(Row(1u, 0u, ChildSpellId, 100u));
            Spell4Entry childEntry = data.Spells[ChildSpellId].Entry;
            childEntry.InnateCostType0 = (uint)Vital.Resource1;
            childEntry.InnateCost0 = 25u;
            childEntry.SpellCoolDown = 2000u;
            data.Spells[ChildSpellId] = CreateSpellInfo(
                childEntry,
                CastMethod.RapidTap,
                [effect],
                new SpellCoolDownEntry { CooldownTime = 1500u });
            SpellRapidTap child = null;
            var spell = new TestChargeRelease(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                childParameters.SpellInfo = data.Spells[spell4Id];
                child = new SpellRapidTap(player.Object, childParameters);
                child.Cast();
                return child;
            });

            ISpellInfo admittedRoot = parameters.RootSpellInfo;
            spell.Cast();
            parameters.RootSpellInfo = CreateSpellInfo(new Spell4Entry { Id = 999u }, CastMethod.Normal);
            spell.Update(0d);
            spell.TryHandleThresholdInput(false);
            Assert.NotNull(child);
            child.Update(-1d);
            child.Update(double.NaN);
            child.Update(double.PositiveInfinity);
            Assert.Equal(0, effectCount);
            child.Update(0d);

            Assert.True(child.Parameters.IsThresholdChild);
            Assert.True(child.Parameters.UserInitiatedSpellCast);
            Assert.Same(admittedRoot, child.Parameters.RootSpellInfo);
            Assert.Equal(100f, values[Vital.Resource1]);
            Assert.Equal(93f, values[Vital.Focus]);
            Assert.Equal([(Vital.Focus, -7f)], mutations);
            Assert.Equal(1, effectCount);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetGlobalSpellCooldown(It.IsAny<double>()), Times.Never);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(ChildSpellId, It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public void ChargeRelease_UsesInclusiveBoundariesAndCumulativeAliasCostsOnce()
        {
            var lifecycle = new List<object>();
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _, values, mutations);
            SpellParameters parameters = CreateRootParameters(CastMethod.ChargeRelease, thresholdTime: 600u);
            ThresholdData data = CreateData(
                Row(1u, 0u, 201u, 100u, Vital.Resource1, 10u),
                Row(2u, 1u, 202u, 200u, Vital.KineticCell, 20u),
                Row(3u, 2u, 203u, 300u, Vital.Resource1, 30u));
            FakeSpell child = null;
            var spell = new TestChargeRelease(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                lifecycle.Add("child");
                childParameters.SpellInfo = data.Spells[spell4Id];
                child = new FakeSpell(player.Object, childParameters);
                return child;
            });

            spell.Cast();
            spell.Update(0d);
            spell.Update(0.299999d);
            Assert.DoesNotContain(lifecycle, value => value is Server0817);

            spell.Update(0.000001d);
            Assert.Equal((byte)2, Assert.Single(lifecycle.OfType<Server0817>()).Unknown0);

            spell.Update(0.3d);

            Assert.NotNull(child);
            Assert.Equal((byte)3, child.Parameters.ThresholdValue);
            Assert.Equal(40f, values[Vital.Resource1]);
            Assert.Single(mutations);
            Assert.Equal((Vital.Resource1, -60f), mutations[0]);
            Assert.True(spell.IsFinishing);
            Assert.Collection(
                lifecycle,
                value => Assert.IsType<Server0816>(value),
                value => Assert.Equal((byte)2, Assert.IsType<Server0817>(value).Unknown0),
                value => Assert.Equal((byte)3, Assert.IsType<Server0817>(value).Unknown0),
                value => Assert.Equal("child", value),
                value => Assert.IsType<Server0814>(value));

            spell.TryHandleThresholdInput(false);
            spell.Update(1d);
            Assert.Single(mutations);
        }

        [Fact]
        public void ChargeRelease_EarlyReleaseIsLatchedUntilParentReadyAndDispatchesOnce()
        {
            var lifecycle = new List<object>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _);
            SpellParameters parameters = CreateRootParameters(
                CastMethod.ChargeRelease,
                thresholdTime: 500u,
                castTime: 100u);
            ThresholdData data = CreateData(Row(1u, 0u, ChildSpellId, 100u));
            int starts = 0;
            var spell = new TestChargeRelease(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                starts++;
                childParameters.SpellInfo = data.Spells[spell4Id];
                return new FakeSpell(player.Object, childParameters);
            });

            spell.Cast();
            spell.TryHandleThresholdInput(false);
            Assert.Equal(0, starts);

            spell.Update(0.1d);
            spell.TryHandleThresholdInput(false);
            spell.Update(1d);

            Assert.Equal(1, starts);
            Assert.True(spell.IsFinishing);
            Assert.Single(lifecycle, value => value is Server0816);
            Assert.Single(lifecycle, value => value is Server0817);
            Assert.Single(lifecycle, value => value is Server0814);
        }

        [Fact]
        public void ChargeRelease_ReentrantReleaseDuringPromotionWinsOnceAgainstTimeout()
        {
            var lifecycle = new List<object>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out Mock<IGameSession> session, out _);
            SpellParameters parameters = CreateRootParameters(CastMethod.ChargeRelease, thresholdTime: 200u);
            ThresholdData data = CreateData(
                Row(1u, 0u, 201u, 100u),
                Row(2u, 1u, 202u, 100u));
            int starts = 0;
            TestChargeRelease spell = null;
            spell = new TestChargeRelease(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                lifecycle.Add("child");
                starts++;
                childParameters.SpellInfo = data.Spells[spell4Id];
                return new FakeSpell(player.Object, childParameters);
            });
            session.Setup(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(packet =>
                {
                    lifecycle.Add(packet);
                    if (packet is Server0817)
                        spell.TryHandleThresholdInput(false);
                });

            spell.Cast();
            spell.Update(0d);
            spell.Update(0.2d);
            spell.TryHandleThresholdInput(false);
            spell.Update(1d);

            Assert.Equal(1, starts);
            Assert.Equal((byte)2, Assert.Single(lifecycle.OfType<Server0817>()).Unknown0);
            Assert.Single(lifecycle, value => value is Server0814);
            Assert.Collection(
                lifecycle,
                value => Assert.IsType<Server0816>(value),
                value => Assert.IsType<Server0817>(value),
                value => Assert.Equal("child", value),
                value => Assert.IsType<Server0814>(value));
        }

        [Fact]
        public void ChargeRelease_PostStartCostRaceStopsChildAndFailsWithoutMutation()
        {
            var lifecycle = new List<object>();
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 10f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _, values, mutations);
            SpellParameters parameters = CreateRootParameters(CastMethod.ChargeRelease, thresholdTime: 500u);
            ThresholdData data = CreateData(Row(1u, 0u, ChildSpellId, 100u, Vital.Resource1, 10u));
            FakeSpell child = null;
            var spell = new TestChargeRelease(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                lifecycle.Add("child");
                childParameters.SpellInfo = data.Spells[spell4Id];
                values[Vital.Resource1] = 9f;
                child = new FakeSpell(player.Object, childParameters)
                {
                    ThrowOnCancel = true
                };
                return child;
            });

            spell.Cast();
            spell.Update(0d);
            Exception exception = Record.Exception(() => spell.TryHandleThresholdInput(false));

            Assert.Null(exception);
            Assert.NotNull(child);
            Assert.Equal(1, child.CancelCount);
            Assert.Equal(1, child.FinishCount);
            Assert.True(child.IsFinished);
            Assert.Empty(mutations);
            Assert.True(spell.IsFinishing);
            Assert.Single(lifecycle, value => value is Server0814);
            Assert.DoesNotContain(lifecycle, value => value is Server0817);
        }

        [Fact]
        public void ChargeRelease_ChildStartExceptionFailsClosedAndClearsOnce()
        {
            var lifecycle = new List<object>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _);
            SpellParameters parameters = CreateRootParameters(CastMethod.ChargeRelease, thresholdTime: 500u);
            ThresholdData data = CreateData(Row(1u, 0u, ChildSpellId, 100u));
            int starts = 0;
            var spell = new TestChargeRelease(player.Object, parameters, data, (_, _) =>
            {
                starts++;
                throw new InvalidOperationException("Test child start failure.");
            });

            spell.Cast();
            spell.Update(0d);
            Exception exception = Record.Exception(() => spell.TryHandleThresholdInput(false));
            spell.TryHandleThresholdInput(false);

            Assert.Null(exception);
            Assert.Equal(1, starts);
            Assert.True(spell.IsFinishing);
            Assert.Single(lifecycle, value => value is Server0814);
            Assert.DoesNotContain(lifecycle, value => value is Server0817);
        }

        [Theory]
        [InlineData(typeof(Server0816))]
        [InlineData(typeof(Server0817))]
        [InlineData(typeof(Server0814))]
        public void ThresholdPacketExceptionsAreBestEffortAndDoNotDuplicateChild(Type packetType)
        {
            var lifecycle = new List<object>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out Mock<IGameSession> session, out _);
            session.Setup(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(packet =>
                {
                    lifecycle.Add(packet);
                    if (packet.GetType() == packetType)
                        throw new InvalidOperationException("Test packet failure.");
                });
            SpellParameters parameters = CreateRootParameters(CastMethod.ChargeRelease, thresholdTime: 500u);
            ThresholdData data = CreateData(Row(1u, 0u, ChildSpellId, 100u));
            int childCount = 0;
            var spell = new TestChargeRelease(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                childCount++;
                childParameters.SpellInfo = data.Spells[spell4Id];
                return new FakeSpell(player.Object, childParameters);
            });

            spell.Cast();
            spell.Update(0d);
            Exception exception = Record.Exception(() => spell.TryHandleThresholdInput(false));

            Assert.Null(exception);
            Assert.Equal(1, childCount);
            Assert.True(spell.IsFinishing);
            Assert.Single(lifecycle, value => value is Server0816);
            Assert.Single(lifecycle, value => value is Server0817);
            Assert.Single(lifecycle, value => value is Server0814);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AcceptedChild_IsStoppedOnceAcrossParentCleanupPaths(int cleanupPath)
        {
            var lifecycle = new List<object>();
            Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _);
            SpellParameters parameters = CreateRootParameters(CastMethod.RapidTap, thresholdTime: 500u);
            ThresholdData data = CreateData(
                Row(1u, 0u, ChildSpellId, 100u),
                Row(2u, 1u, 201u, 100u));
            FakeSpell child = null;
            var spell = new TestRapidTap(player.Object, parameters, data, (spell4Id, childParameters) =>
            {
                childParameters.SpellInfo = data.Spells[spell4Id];
                child = new FakeSpell(player.Object, childParameters);
                return child;
            });
            spell.Cast();
            spell.Update(0d);
            spell.TryHandleThresholdInput(true);

            void Cleanup()
            {
                switch (cleanupPath)
                {
                    case 0:
                        spell.CancelCast(CastResult.SpellCancelled);
                        break;
                    case 1:
                        spell.Finish();
                        break;
                    case 2:
                        spell.Dispose();
                        break;
                }
            }

            Cleanup();
            Cleanup();

            Assert.NotNull(child);
            Assert.True(child.IsFinished);
            Assert.Equal(1, child.CancelCount);
            Assert.Equal(0, child.FinishCount);
            Assert.Single(lifecycle, value => value is Server0814);
        }

        [Fact]
        public void MalformedAndUnsupportedRowsFailClosedBeforeOpeningThresholdWindow()
        {
            Spell4ThresholdsEntry nestedChild = Row(2u, 0u, 300u, 100u);
            nestedChild.Spell4IdParent = ChildSpellId;
            var missingChild = CreateData(Row(1u, 0u, ChildSpellId, 100u));
            missingChild.Spells.Clear();
            InvalidConfiguration[] configurations =
            [
                new(CastMethod.RapidTap, 500u, CreateData()),
                new(CastMethod.RapidTap, 500u, CreateData(Row(1u, 1u, ChildSpellId, 100u))),
                new(CastMethod.RapidTap, 500u, CreateData(
                    Row(1u, 0u, ChildSpellId, 100u),
                    Row(1u, 1u, 201u, 100u))),
                new(CastMethod.RapidTap, 500u, CreateData(
                    Row(1u, 0u, ChildSpellId, 100u),
                    Row(2u, 0u, 201u, 100u))),
                new(CastMethod.RapidTap, 500u, CreateData(Row(1u, 0u, RootSpellId, 100u))),
                new(CastMethod.RapidTap, 500u, missingChild),
                new(CastMethod.RapidTap, 500u, CreateData(CastMethod.Multiphase,
                    Row(1u, 0u, ChildSpellId, 100u))),
                new(CastMethod.RapidTap, 500u, CreateData(
                    Row(1u, 0u, ChildSpellId, 100u),
                    nestedChild)),
                new(CastMethod.RapidTap, 500u, CreateData(
                    Row(1u, 0u, ChildSpellId, 100u, Vital.Resource1, 1u))),
                new(CastMethod.RapidTap, 0u, CreateData(Row(1u, 0u, ChildSpellId, 100u))),
                new(CastMethod.ChargeRelease, 500u, CreateData(Row(1u, 0u, ChildSpellId, 501u))),
                new(CastMethod.ChargeRelease, 500u, CreateData(
                    Row(1u, 0u, ChildSpellId, 100u, unchecked((Vital)999u), 1u))),
                new(CastMethod.ChargeRelease, 500u, CreateData(
                    Row(1u, 0u, ChildSpellId, 100u, Vital.Resource1, uint.MaxValue)))
            ];

            foreach (InvalidConfiguration configuration in configurations)
            {
                var lifecycle = new List<object>();
                Mock<IPlayer> player = CreatePlayer(lifecycle, out _, out _);
                SpellParameters parameters = CreateRootParameters(
                    configuration.Method,
                    configuration.ThresholdTime);
                int childCount = 0;
                SpellThreshold spell = configuration.Method == CastMethod.RapidTap
                    ? new TestRapidTap(player.Object, parameters, configuration.Data, (_, _) =>
                    {
                        childCount++;
                        return null;
                    })
                    : new TestChargeRelease(player.Object, parameters, configuration.Data, (_, _) =>
                    {
                        childCount++;
                        return null;
                    });

                spell.Cast();
                spell.Update(1d);

                Assert.True(spell.IsFinishing);
                Assert.Equal(0, childCount);
                Assert.DoesNotContain(lifecycle, value => value is Server0816);
            }
        }

        [Fact]
        public void CharacterSpell_RoutesOnlyExactActiveRootAndNeverCreatesOnRelease()
        {
            var owner = new Mock<IPlayer>();
            ISpell active = null;
            owner.Setup(player => player.GetActiveSpell(It.IsAny<Func<ISpell, bool>>()))
                .Returns((Func<ISpell, bool> predicate) => active != null && predicate(active) ? active : null);
            ISpellInfo spellInfo = CreateSpellInfo(new Spell4Entry { Id = RootSpellId }, CastMethod.ChargeRelease);
            var baseInfo = new Mock<ISpellBaseInfo>();
            baseInfo.SetupGet(info => info.Entry).Returns(spellInfo.BaseInfo.Entry);
            baseInfo.Setup(info => info.GetSpellInfo(1)).Returns(spellInfo);
            var characterSpell = new CharacterSpell(owner.Object, baseInfo.Object, 1, null);
            var parameters = new SpellParameters
            {
                CharacterSpell = characterSpell,
                SpellInfo = spellInfo,
                RootSpellInfo = spellInfo
            };
            var activeRoot = new Mock<ISpell>();
            activeRoot.SetupGet(spell => spell.Parameters).Returns(parameters);
            activeRoot.SetupGet(spell => spell.IsFinished).Returns(false);
            var thresholdRoot = activeRoot.As<IThresholdSpell>();
            thresholdRoot.Setup(spell => spell.TryHandleThresholdInput(It.IsAny<bool>())).Returns(true);
            active = activeRoot.Object;

            characterSpell.Cast(false);

            thresholdRoot.Verify(spell => spell.TryHandleThresholdInput(false), Times.Once);
            owner.Verify(player => player.CastSpell(It.IsAny<ISpellParameters>()), Times.Never);

            active = null;
            characterSpell.Cast(false);
            owner.Verify(player => player.CastSpell(It.IsAny<ISpellParameters>()), Times.Never);

            characterSpell.Cast(true);
            owner.Verify(player => player.CastSpell(It.Is<ISpellParameters>(cast =>
                ReferenceEquals(cast.CharacterSpell, characterSpell)
                && ReferenceEquals(cast.SpellInfo, spellInfo)
                && cast.UserInitiatedSpellCast)), Times.Once);
        }

        [Fact]
        public void UnitEntity_StartFailurePreservesOriginalAndIsolatesFinishAndDisposeFailures()
        {
            var owner = new TestUnitEntity(Mock.Of<IMovementManager>());
            var sibling = new AlwaysEqualSpell(owner, new SpellParameters());
            owner.AddPending(sibling);
            var original = new InvalidOperationException("Cast failed.");
            var spell = new ThrowingSpell(owner, original)
            {
                ThrowOnFinish = true,
                ThrowOnDispose = true
            };
            spell.OnCast = () => Assert.Contains(owner.PendingSpells, candidate => ReferenceEquals(candidate, spell));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => owner.Start(spell));

            Assert.Same(original, exception);
            Assert.Equal(1, spell.FinishCount);
            Assert.Equal(1, spell.DisposeCount);
            Assert.Collection(owner.PendingSpells, candidate => Assert.Same(sibling, candidate));
        }

        [Fact]
        public void UnitEntity_CopiedUserInitiatedMarkerDismountsOnlyTheRoot()
        {
            var owner = new TestUnitEntity(Mock.Of<IMovementManager>());

            owner.ApplyUserInitiatedCastSideEffects(new SpellParameters
            {
                UserInitiatedSpellCast = true
            });
            owner.ApplyUserInitiatedCastSideEffects(new SpellParameters
            {
                UserInitiatedSpellCast = true,
                IsThresholdChild = true
            });

            Assert.Equal(1, owner.DismountCount);
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        private static void AssertSpellType<TSpell>(CastMethod expected) where TSpell : NexusForever.Game.Spell.Spell
        {
            SpellTypeAttribute attribute = typeof(TSpell).GetCustomAttribute<SpellTypeAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(expected, attribute.CastMethod);
        }

        private static Spell4ThresholdsEntry Row(
            uint id,
            uint order,
            uint childSpellId,
            uint duration,
            Vital costType = Vital.Invalid,
            uint cost = 0u)
        {
            return new Spell4ThresholdsEntry
            {
                Id                  = id,
                Spell4IdParent      = RootSpellId,
                Spell4IdToCast      = childSpellId,
                OrderIndex          = order,
                ThresholdDuration   = duration,
                VitalEnumCostType00 = (uint)costType,
                VitalCostValue00    = cost
            };
        }

        private static ThresholdData CreateData(params Spell4ThresholdsEntry[] rows)
        {
            return CreateData(CastMethod.Normal, rows);
        }

        private static ThresholdData CreateData(
            CastMethod childMethod,
            params Spell4ThresholdsEntry[] rows)
        {
            Dictionary<uint, ISpellInfo> spells = rows
                .Select(row => row.Spell4IdToCast)
                .Where(id => id != 0u && id != RootSpellId)
                .Distinct()
                .ToDictionary(
                    id => id,
                    id => CreateSpellInfo(new Spell4Entry
                    {
                        Id = id,
                        Spell4BaseIdBaseSpell = id + 1000u,
                        TierIndex = 1u
                    }, childMethod));
            return new ThresholdData(rows, spells);
        }

        private static SpellParameters CreateRootParameters(
            CastMethod method,
            uint thresholdTime,
            uint castTime = 0u,
            uint primaryTargetId = 0u,
            Position position = null,
            bool userInitiated = false,
            bool procTriggered = false)
        {
            ISpellInfo spellInfo = CreateSpellInfo(new Spell4Entry
            {
                Id = RootSpellId,
                CastTime = castTime,
                ThresholdTime = thresholdTime
            }, method);
            return new SpellParameters
            {
                SpellInfo = spellInfo,
                RootSpellInfo = spellInfo,
                PrimaryTargetId = primaryTargetId,
                Position = position,
                TaxiNode = 7,
                UserInitiatedSpellCast = userInitiated,
                IsProcTriggered = procTriggered
            };
        }

        private static ISpellInfo CreateSpellInfo(
            Spell4Entry entry,
            CastMethod method,
            List<Spell4EffectsEntry> effects = null,
            SpellCoolDownEntry globalCooldown = null)
        {
            var baseInfo = new Mock<ISpellBaseInfo>();
            baseInfo.SetupGet(info => info.Entry).Returns(new Spell4BaseEntry
            {
                Id = entry.Spell4BaseIdBaseSpell,
                CastMethod = (uint)method
            });
            var spellInfo = new Mock<ISpellInfo>();
            spellInfo.SetupGet(info => info.Entry).Returns(entry);
            spellInfo.SetupGet(info => info.BaseInfo).Returns(baseInfo.Object);
            spellInfo.SetupGet(info => info.GlobalCooldown).Returns(globalCooldown);
            spellInfo.SetupGet(info => info.Effects).Returns(effects ?? []);
            spellInfo.SetupGet(info => info.Telegraphs).Returns([]);
            spellInfo.SetupGet(info => info.PrerequisiteRunners).Returns([]);
            return spellInfo.Object;
        }

        private void RegisterEffectHandler(SpellEffectType type, SpellEffectDelegate handler)
        {
            GlobalSpellManager manager = serviceProvider.GetRequiredService<GlobalSpellManager>();
            FieldInfo field = typeof(GlobalSpellManager).GetField(
                "spellEffectDelegates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var handlers = (Dictionary<SpellEffectType, SpellEffectDelegate>)field.GetValue(manager);
            handlers[type] = handler;
        }

        private static Mock<IPlayer> CreatePlayer(
            List<object> lifecycle,
            out Mock<IGameSession> session,
            out IBaseMap map,
            Dictionary<Vital, float> values = null,
            List<(Vital Vital, float Delta)> mutations = null)
        {
            values ??= [];
            mutations ??= [];
            session = new Mock<IGameSession>();
            session.Setup(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(lifecycle.Add);
            var spellManager = new Mock<ISpellManager>();
            var mapMock = new Mock<IBaseMap>();
            map = mapMock.Object;
            var player = new Mock<IPlayer>();
            player.SetupGet(unit => unit.Guid).Returns(7u);
            player.SetupGet(unit => unit.IsAlive).Returns(true);
            player.SetupGet(unit => unit.InWorld).Returns(true);
            player.SetupGet(unit => unit.Map).Returns(map);
            player.SetupGet(unit => unit.Position).Returns(new Vector3(10f, 20f, 30f));
            player.SetupGet(unit => unit.Rotation).Returns(Vector3.Zero);
            player.SetupGet(unit => unit.IsLoading).Returns(false);
            player.SetupGet(unit => unit.Session).Returns(session.Object);
            player.SetupGet(unit => unit.SpellManager).Returns(spellManager.Object);
            player.Setup(unit => unit.TryGetVitalValue(It.IsAny<Vital>(), out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                    values.TryGetValue(Canonical(vital), out value)));
            player.Setup(unit => unit.TryModifyVital(
                    It.IsAny<Vital>(), It.IsAny<float>(), It.IsAny<IUnitEntity>()))
                .Returns((Vital vital, float delta, IUnitEntity _) =>
                {
                    Vital canonical = Canonical(vital);
                    if (!values.TryGetValue(canonical, out float current))
                        return false;

                    mutations.Add((canonical, delta));
                    values[canonical] = current + delta;
                    return true;
                });
            return player;
        }

        private static Vital Canonical(Vital vital)
        {
            return vital switch
            {
                Vital.KineticCell or Vital.StalkerB or Vital.MedicCore or Vital.Volatility => Vital.Resource1,
                Vital.StalkerA => Vital.Resource3,
                Vital.SpellSurge => Vital.Resource4,
                _ => vital
            };
        }

        private sealed record InvalidConfiguration(
            CastMethod Method,
            uint ThresholdTime,
            ThresholdData Data);

        private sealed record ThresholdData(
            IReadOnlyList<Spell4ThresholdsEntry> Rows,
            Dictionary<uint, ISpellInfo> Spells)
        {
            public Spell4Entry GetSpell(uint id)
            {
                return Spells.TryGetValue(id, out ISpellInfo info) ? info.Entry : null;
            }

            public Spell4BaseEntry GetBase(uint id)
            {
                ISpellInfo info = Spells.Values.FirstOrDefault(candidate => candidate.BaseInfo.Entry.Id == id);
                return info?.BaseInfo.Entry;
            }
        }

        private sealed class TestRapidTap : SpellRapidTap
        {
            private readonly ThresholdData data;
            private readonly Func<uint, ISpellParameters, ISpell> startChild;

            public TestRapidTap(
                IUnitEntity caster,
                ISpellParameters parameters,
                ThresholdData data,
                Func<uint, ISpellParameters, ISpell> startChild)
                : base(caster, parameters)
            {
                this.data = data;
                this.startChild = startChild;
            }

            protected override IEnumerable<Spell4ThresholdsEntry> GetThresholdEntries() => data.Rows;
            protected override Spell4Entry GetSpellEntry(uint spell4Id) => data.GetSpell(spell4Id);
            protected override Spell4BaseEntry GetSpellBaseEntry(uint spell4BaseId) => data.GetBase(spell4BaseId);
            protected override ISpell CastThresholdChild(uint spell4Id, ISpellParameters parameters) => startChild(spell4Id, parameters);
        }

        private sealed class TestChargeRelease : SpellChargeRelease
        {
            private readonly ThresholdData data;
            private readonly Func<uint, ISpellParameters, ISpell> startChild;

            public TestChargeRelease(
                IUnitEntity caster,
                ISpellParameters parameters,
                ThresholdData data,
                Func<uint, ISpellParameters, ISpell> startChild)
                : base(caster, parameters)
            {
                this.data = data;
                this.startChild = startChild;
            }

            protected override IEnumerable<Spell4ThresholdsEntry> GetThresholdEntries() => data.Rows;
            protected override Spell4Entry GetSpellEntry(uint spell4Id) => data.GetSpell(spell4Id);
            protected override Spell4BaseEntry GetSpellBaseEntry(uint spell4BaseId) => data.GetBase(spell4BaseId);
            protected override ISpell CastThresholdChild(uint spell4Id, ISpellParameters parameters) => startChild(spell4Id, parameters);
        }

        private class FakeSpell : ISpell
        {
            public ISpellParameters Parameters { get; }
            public uint CastingId => 999u;
            public bool IsCasting => !IsFinished;
            public bool IsFinished { get; private set; }
            public bool IsFinishing => false;
            public bool IsWaiting => false;
            public IUnitEntity Caster { get; }
            public int CancelCount { get; private set; }
            public int FinishCount { get; private set; }
            public bool ThrowOnCancel { get; init; }

            public FakeSpell(IUnitEntity caster, ISpellParameters parameters)
            {
                Caster = caster;
                Parameters = parameters;
            }

            public virtual void Cast()
            {
            }

            public virtual void CancelCast(CastResult result)
            {
                CancelCount++;
                if (ThrowOnCancel)
                    throw new InvalidOperationException("Test cancel failure.");

                IsFinished = true;
            }

            public virtual void Finish()
            {
                FinishCount++;
                IsFinished = true;
            }

            public virtual void Dispose()
            {
            }

            public void Update(double lastTick)
            {
            }

            public void LateUpdate(double lastTick)
            {
            }

            public bool IsMovingInterrupted() => false;

            public void TrackProc(IUnitEntity target, IProcInfo proc)
            {
            }
        }

        private sealed class ThrowingSpell : FakeSpell
        {
            private readonly Exception castException;

            public Action OnCast { get; set; }
            public bool ThrowOnFinish { get; init; }
            public bool ThrowOnDispose { get; init; }
            public int DisposeCount { get; private set; }

            public ThrowingSpell(IUnitEntity caster, Exception castException)
                : base(caster, new SpellParameters())
            {
                this.castException = castException;
            }

            public override void Cast()
            {
                OnCast?.Invoke();
                throw castException;
            }

            public override void Finish()
            {
                base.Finish();
                if (ThrowOnFinish)
                    throw new InvalidOperationException("Test finish failure.");
            }

            public override void Dispose()
            {
                DisposeCount++;
                if (ThrowOnDispose)
                    throw new InvalidOperationException("Test dispose failure.");
            }
        }

        private sealed class AlwaysEqualSpell : FakeSpell
        {
            public AlwaysEqualSpell(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            public override bool Equals(object obj) => obj is ISpell;
            public override int GetHashCode() => 0;
        }

        private sealed class TestUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;
            public int DismountCount { get; private set; }

            public IReadOnlyList<ISpell> PendingSpells
            {
                get
                {
                    FieldInfo pendingSpellsField = typeof(UnitEntity).GetField(
                        "pendingSpells",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    return ((List<ISpell>)pendingSpellsField.GetValue(this)).ToArray();
                }
            }

            public TestUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public ISpell Start(ISpell spell) => StartAndTrackSpell(spell);

            public void AddPending(ISpell spell)
            {
                FieldInfo pendingSpellsField = typeof(UnitEntity).GetField(
                    "pendingSpells",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                ((List<ISpell>)pendingSpellsField.GetValue(this)).Add(spell);
            }

            protected override float CalculateDefaultProperty(Property property) => 0f;
            protected override IEntityModel BuildEntityModel() => new NonPlayerEntityModel();
            protected override void DismountForUserInitiatedCast() => DismountCount++;
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
    }
}
