using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Entity;
using NexusForever.Game.Spell;
using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
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
    public sealed class SpellVitalExecutionTests : IDisposable
    {
        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public SpellVitalExecutionTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var scriptCollection = new Mock<IScriptCollection>();
            var scriptManager = new Mock<IScriptManager>();
            scriptManager.Setup(manager => manager.InitialiseOwnedScripts<ISpell>(
                    It.IsAny<ISpell>(), It.IsAny<uint>()))
                .Returns(scriptCollection.Object);
            var entityManager = new EntityManager();
            typeof(EntityManager)
                .GetMethod("InitialiseEntityStats", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entityManager, null);

            serviceProvider = new ServiceCollection()
                .AddSingleton(scriptManager.Object)
                .AddSingleton(new GlobalSpellManager())
                .AddSingleton(entityManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        [Fact]
        public void Execute_RechecksRequirementBeforeAnyCostCooldownChargeOrEffect()
        {
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id                              = 123u,
                    SpellCoolDown                   = 1000u,
                    CasterInnateRequirement0        = (uint)Vital.Resource1,
                    CasterInnateRequirementValue0   = 50u,
                    CasterInnateRequirementEval0    = 2u,
                    InnateCostType0                 = (uint)Vital.Resource1,
                    InnateCost0                     = 10u
                },
                characterSpell.Object);
            var spell = new TestSpell(player.Object, parameters);

            Assert.Equal(CastResult.Ok, spell.CheckCastForTest());
            values[Vital.Resource1] = 40f;
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();

            Assert.True(spell.IsFinishing);
            Assert.Equal(0, spell.TargetSelectionCount);
            Assert.Empty(mutations);
            characterSpell.Verify(character => character.UseCharge(), Times.Never);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
        }

        [Fact]
        public void Cast_CommitTimeFailureRetainsStartTimeGlobalCooldownWithoutSpellCooldownOrCharge()
        {
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 50f };
            Mock<IPlayer> player = CreatePlayer(values);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id                            = 123u,
                    SpellCoolDown                 = 1000u,
                    CasterInnateRequirement0      = (uint)Vital.Resource1,
                    CasterInnateRequirementValue0 = 50u,
                    CasterInnateRequirementEval0  = 2u
                },
                characterSpell.Object,
                1500u);
            var spell = new TestSpell(player.Object, parameters);

            spell.Cast();
            values[Vital.Resource1] = 49f;
            spell.Update(0d);

            Assert.True(spell.IsFinishing);
            characterSpell.Verify(character => character.UseCharge(), Times.Never);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetGlobalSpellCooldown(1.5d),
                Times.Once);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
        }

        [Fact]
        public void Execute_CompetingCooldownCommitFailsBeforeVitalChargeOrEffect()
        {
            double cooldown = 0d;
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            Mock.Get(player.Object.SpellManager)
                .Setup(manager => manager.GetSpellCooldown(123u))
                .Returns(() => cooldown);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id              = 123u,
                    SpellCoolDown   = 1000u,
                    InnateCostType0 = (uint)Vital.Resource1,
                    InnateCost0     = 25u
                },
                characterSpell.Object);
            var spell = new TestSpell(player.Object, parameters);

            Assert.Equal(CastResult.Ok, spell.CheckCastForTest());
            cooldown = 1d;
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();

            Assert.True(spell.IsFinishing);
            Assert.Empty(mutations);
            Assert.Equal(0, spell.TargetSelectionCount);
            characterSpell.Verify(character => character.UseCharge(), Times.Never);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
        }

        [Fact]
        public void Execute_CompetingChargeCommitFailsBeforeVitalCooldownOrEffect()
        {
            uint charges = 1u;
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            characterSpell.SetupGet(character => character.AbilityCharges).Returns(() => charges);
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id              = 123u,
                    SpellCoolDown   = 1000u,
                    InnateCostType0 = (uint)Vital.Resource1,
                    InnateCost0     = 25u
                },
                characterSpell.Object);
            var spell = new TestSpell(player.Object, parameters);

            Assert.Equal(CastResult.Ok, spell.CheckCastForTest());
            charges = 0u;
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();

            Assert.True(spell.IsFinishing);
            Assert.Empty(mutations);
            Assert.Equal(0, spell.TargetSelectionCount);
            characterSpell.Verify(character => character.UseCharge(), Times.Never);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
        }

        [Fact]
        public void Execute_AbilityChargeFailureBeforeMutationCleansUpWithoutVitalOrCooldown()
        {
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            characterSpell.Setup(character => character.UseCharge())
                .Throws(new InvalidOperationException("Test charge failure."));
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id              = 123u,
                    SpellCoolDown   = 1000u,
                    InnateCostType0 = (uint)Vital.Resource1,
                    InnateCost0     = 25u
                },
                characterSpell.Object);
            var spell = new TestSpell(player.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();

            Assert.True(spell.IsFinishing);
            Assert.Empty(mutations);
            Assert.Equal(0, spell.TargetSelectionCount);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
        }

        [Fact]
        public void Execute_AbilityChargeNotificationFailureKeepsCommittedTransaction()
        {
            uint charges = 1u;
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            characterSpell.SetupGet(character => character.AbilityCharges).Returns(() => charges);
            characterSpell.Setup(character => character.UseCharge())
                .Callback(() => charges--)
                .Throws(new InvalidOperationException("Test notification failure."));
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id              = 123u,
                    SpellCoolDown   = 1000u,
                    InnateCostType0 = (uint)Vital.Resource1,
                    InnateCost0     = 25u
                },
                characterSpell.Object);
            var spell = new TestSpell(player.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();

            Assert.Equal(0u, charges);
            Assert.Equal(75f, values[Vital.Resource1]);
            Assert.Equal([(Vital.Resource1, -25f)], mutations);
            Assert.Equal(1, spell.TargetSelectionCount);
            Assert.False(spell.IsFinishing);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(123u, 1d),
                Times.Once);
        }

        [Fact]
        public void Execute_VitalMutationExceptionFailsWithDeterministicCleanup()
        {
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            player.Setup(unit => unit.TryModifyVital(
                    It.IsAny<Vital>(),
                    It.IsAny<float>(),
                    It.IsAny<IUnitEntity>()))
                .Throws(new InvalidOperationException("Test vital mutation failure."));
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id              = 123u,
                    SpellCoolDown   = 1000u,
                    InnateCostType0 = (uint)Vital.Resource1,
                    InnateCost0     = 25u
                },
                characterSpell.Object);
            var spell = new TestSpell(player.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();

            Assert.True(spell.IsFinishing);
            Assert.Equal(100f, values[Vital.Resource1]);
            Assert.Empty(mutations);
            Assert.Equal(0, spell.TargetSelectionCount);
            characterSpell.Verify(character => character.UseCharge(), Times.Once);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
        }

        [Fact]
        public void Execute_RepeatedPulsesConsumeVitalEachTimeButCommitCooldownAndChargeOnce()
        {
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            SpellParameters parameters = CreateParameters(
                CastMethod.Channeled,
                new Spell4Entry
                {
                    Id              = 123u,
                    SpellCoolDown   = 1000u,
                    InnateCostType0 = (uint)Vital.Resource1,
                    InnateCost0     = 25u
                },
                characterSpell.Object);
            var spell = new TestSpell(player.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();
            spell.ExecuteForTest();

            Assert.Equal(50f, values[Vital.Resource1]);
            Assert.Equal(
                [(Vital.Resource1, -25f), (Vital.Resource1, -25f)],
                mutations);
            Assert.Equal(2, spell.TargetSelectionCount);
            characterSpell.Verify(character => character.UseCharge(), Times.Once);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(123u, 1d),
                Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ChanneledAndField_InsufficientLaterPulseCancelsRemainingWorkWithoutSecondCharge(
            bool channeledField)
        {
            var values = new Dictionary<Vital, float> { [Vital.Resource1] = 100f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            Mock<ICharacterSpell> characterSpell = CreateCharacterSpell();
            SpellParameters parameters = CreateParameters(
                channeledField ? CastMethod.ChanneledField : CastMethod.Channeled,
                new Spell4Entry
                {
                    Id                  = 123u,
                    InnateCostType0     = (uint)Vital.Resource1,
                    InnateCost0         = 60u,
                    ChannelInitialDelay = 0u,
                    ChannelPulseTime    = 1000u,
                    ChannelMaxTime      = 2000u
                },
                characterSpell.Object);
            NexusForever.Game.Spell.Spell spell = channeledField
                ? new SpellChanneledField(player.Object, parameters)
                : new SpellChanneled(player.Object, parameters);

            spell.Cast();
            spell.Update(0d);
            spell.Update(1d);

            Assert.True(spell.IsFinishing);
            Assert.Equal(40f, values[Vital.Resource1]);
            Assert.Equal([(Vital.Resource1, -60f)], mutations);
            characterSpell.Verify(character => character.UseCharge(), Times.Once);

            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void Execute_MultiphaseFollowUpSkipsBaseCost()
        {
            var values = new Dictionary<Vital, float> { [Vital.Focus] = 30f };
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(values, mutations);
            SpellParameters parameters = CreateParameters(
                CastMethod.Multiphase,
                new Spell4Entry
                {
                    Id              = 123u,
                    InnateCostType0 = (uint)Vital.Focus,
                    InnateCost0     = 30u
                });
            var spell = new TestSpell(player.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest(true);
            spell.ExecuteForTest(false);

            Assert.Equal(0f, values[Vital.Focus]);
            Assert.Equal([(Vital.Focus, -30f)], mutations);
        }

        [Fact]
        public void Aura_ExecutionCommitsSpellCooldownOnce()
        {
            Mock<IPlayer> player = CreatePlayer([]);
            SpellParameters parameters = CreateParameters(
                CastMethod.Aura,
                new Spell4Entry
                {
                    Id            = 123u,
                    SpellCoolDown = 1000u
                },
                globalCooldown: 1500u);
            var spell = new SpellAura(player.Object, parameters);

            spell.Cast();
            spell.Update(0d);

            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetSpellCooldown(123u, 1d),
                Times.Once);
            Mock.Get(player.Object.SpellManager).Verify(
                manager => manager.SetGlobalSpellCooldown(1.5d),
                Times.Once);
        }

        [Fact]
        public void NpcCast_EvaluatesHealthRequirementButDoesNotConsumePlayerVitalCost()
        {
            var caster = new Mock<IUnitEntity>();
            caster.Setup(unit => unit.TryGetVitalValue(
                    Vital.Health,
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital _, out float value) =>
                {
                    value = 75f;
                    return true;
                }));
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id                            = 123u,
                    CasterInnateRequirement0      = (uint)Vital.Health,
                    CasterInnateRequirementValue0 = 50u,
                    CasterInnateRequirementEval0  = 2u,
                    InnateCostType0               = (uint)Vital.Resource1,
                    InnateCost0                   = 100u
                });
            var spell = new TestSpell(caster.Object, parameters);

            Assert.Equal(CastResult.Ok, spell.CheckCastForTest());
            spell.SetStatus(SpellStatus.Casting);
            spell.ExecuteForTest();

            Assert.Equal(1, spell.TargetSelectionCount);
            caster.Verify(unit => unit.TryModifyVital(
                It.IsAny<Vital>(), It.IsAny<float>(), It.IsAny<IUnitEntity>()), Times.Never);
        }

        [Fact]
        public void CheckCast_TargetBeginRequirementUsesResolvedPrimaryTarget()
        {
            var caster = new Mock<IUnitEntity>();
            var target = new Mock<IUnitEntity>();
            caster.Setup(unit => unit.GetVisible<IWorldEntity>(42u)).Returns(target.Object);
            target.Setup(unit => unit.TryGetVitalValue(
                    Vital.Health,
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital _, out float value) =>
                {
                    value = 20f;
                    return true;
                }));
            target.Setup(unit => unit.TryGetVitalMaximum(
                    Vital.Health,
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalMaximum((Vital _, out float maximum) =>
                {
                    maximum = 100f;
                    return true;
                }));
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id                                    = 123u,
                    TargetBeginInnateRequirement          = (uint)Vital.Health,
                    TargetBeginInnateRequirementValue     = 35u,
                    TargetBeginInnateRequirementEval      = 9u
                });
            parameters.PrimaryTargetId = 42u;
            var spell = new TestSpell(caster.Object, parameters);

            Assert.Equal(CastResult.Ok, spell.CheckCastForTest());
        }

        [Fact]
        public void CheckCast_MissingRequiredPrimaryTargetFailsTargetUnknown()
        {
            var caster = new Mock<IUnitEntity>();
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id                           = 123u,
                    TargetBeginInnateRequirement = (uint)Vital.Health
                });
            parameters.PrimaryTargetId = 42u;
            var spell = new TestSpell(caster.Object, parameters);

            Assert.Equal(CastResult.TargetUnknown, spell.CheckCastForTest());
        }

        [Fact]
        public void ThresholdParentBaseCostUsesTheNormalSingleExecutionTransaction()
        {
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(
                new Dictionary<Vital, float> { [Vital.Focus] = 100f },
                mutations);
            SpellParameters parameters = CreateParameters(
                CastMethod.RapidTap,
                new Spell4Entry
                {
                    Id              = 123u,
                    InnateCostType0 = (uint)Vital.Focus,
                    InnateCost0     = 10u
                });
            var spell = new TestSpell(player.Object, parameters);

            spell.Cast();
            spell.Update(0d);

            Assert.Equal([(Vital.Focus, -10f)], mutations);
            Assert.False(spell.IsFinishing);
        }

        [Fact]
        public void InvalidThresholdChildMetadataFailsBeforeAnyVitalMutation()
        {
            var mutations = new List<(Vital Vital, float Delta)>();
            Mock<IPlayer> player = CreatePlayer(
                new Dictionary<Vital, float> { [Vital.Focus] = 100f },
                mutations);
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id              = 123u,
                    InnateCostType0 = (uint)Vital.Focus,
                    InnateCost0     = 10u
                });
            parameters.IsThresholdChild = true;
            parameters.ThresholdValue = 1;
            var spell = new TestSpell(player.Object, parameters);

            spell.Cast();

            Assert.True(spell.IsFinishing);
            Assert.Empty(mutations);
        }

        [Fact]
        public void ExecutionFailure_SendsSpecificResultThenCancelPacket()
        {
            var packets = new List<IWritable>();
            var session = new Mock<IGameSession>();
            session.Setup(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(packets.Add);
            Mock<IPlayer> player = CreatePlayer(
                new Dictionary<Vital, float> { [Vital.Resource1] = 49f },
                session: session,
                isLoading: false);
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id                            = 123u,
                    CasterInnateRequirement0      = (uint)Vital.Resource1,
                    CasterInnateRequirementValue0 = 50u,
                    CasterInnateRequirementEval0  = 2u
                });
            var spell = new TestSpell(player.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            spell.ExecuteForTest();

            Assert.True(spell.IsFinishing);
            Assert.Collection(
                packets,
                packet =>
                {
                    ServerSpellCastResult result = Assert.IsType<ServerSpellCastResult>(packet);
                    Assert.Equal(123u, result.Spell4Id);
                    Assert.Equal(CastResult.CasterVitalCostResource1, result.CastResult);
                },
                packet =>
                {
                    Server07F9 cancel = Assert.IsType<Server07F9>(packet);
                    Assert.Equal(spell.CastingId, cancel.ServerUniqueId);
                    Assert.Equal(CastResult.CasterVitalCostResource1, cancel.CastResult);
                    Assert.True(cancel.CancelCast);
                });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void ExecutionFailure_PacketEnqueueExceptionIsContainedAndStillCleansUp(int throwingCall)
        {
            int enqueueCount = 0;
            var session = new Mock<IGameSession>();
            session.Setup(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(_ =>
                {
                    enqueueCount++;
                    if (enqueueCount == throwingCall)
                        throw new InvalidOperationException("Test packet failure.");
                });
            Mock<IPlayer> player = CreatePlayer(
                new Dictionary<Vital, float> { [Vital.Resource1] = 49f },
                session: session,
                isLoading: false);
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id                            = 123u,
                    CasterInnateRequirement0      = (uint)Vital.Resource1,
                    CasterInnateRequirementValue0 = 50u,
                    CasterInnateRequirementEval0  = 2u
                });
            var spell = new TestSpell(player.Object, parameters);
            spell.SetStatus(SpellStatus.Casting);

            Exception exception = Record.Exception(() => spell.ExecuteForTest());

            Assert.Null(exception);
            Assert.Equal(2, enqueueCount);
            Assert.True(spell.IsFinishing);
            spell.LateUpdate(0d);
            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void InitialFailure_AllSpellVariantsAreRemovedFromPendingSpells()
        {
            var caster = new Mock<IUnitEntity>();
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                new Spell4Entry
                {
                    Id                            = 123u,
                    CasterInnateRequirement0      = (uint)Vital.Breath,
                    CasterInnateRequirementValue0 = 1u,
                    CasterInnateRequirementEval0  = 2u
                });
            NexusForever.Game.Spell.Spell[] spells =
            [
                new TestSpell(caster.Object, parameters),
                new SpellAura(caster.Object, parameters),
                new SpellChanneled(caster.Object, parameters),
                new SpellChanneledField(caster.Object, parameters),
                new SpellMultiphase(caster.Object, parameters),
                new SpellRapidTap(caster.Object, parameters),
                new SpellChargeRelease(caster.Object, parameters),
                new SpellClientSideInteraction(caster.Object, parameters)
            ];
            var owner = new TestPendingUnitEntity(Mock.Of<IMovementManager>());

            foreach (NexusForever.Game.Spell.Spell spell in spells)
            {
                spell.Cast();
                Assert.True(spell.IsFinishing);
                owner.AddPendingSpell(spell);
            }

            owner.Update(0d);

            Assert.All(spells, spell => Assert.True(spell.IsFinished));
            Assert.Equal(0, owner.PendingSpellCount);
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        private static Mock<IPlayer> CreatePlayer(
            Dictionary<Vital, float> values,
            List<(Vital Vital, float Delta)> mutations = null,
            Mock<IGameSession> session = null,
            bool isLoading = true)
        {
            mutations ??= [];
            var spellManager = new Mock<ISpellManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(unit => unit.IsLoading).Returns(isLoading);
            player.SetupGet(unit => unit.Session).Returns(session?.Object);
            player.SetupGet(unit => unit.SpellManager).Returns(spellManager.Object);
            player.Setup(unit => unit.TryGetVitalValue(
                    It.IsAny<Vital>(),
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                {
                    return values.TryGetValue(Canonical(vital), out value);
                }));
            player.Setup(unit => unit.TryGetVitalMaximum(
                    It.IsAny<Vital>(),
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalMaximum((Vital _, out float maximum) =>
                {
                    maximum = 0f;
                    return false;
                }));
            player.Setup(unit => unit.TryModifyVital(
                    It.IsAny<Vital>(),
                    It.IsAny<float>(),
                    It.IsAny<IUnitEntity>()))
                .Returns((Vital vital, float delta, IUnitEntity _) =>
                {
                    Vital canonical = Canonical(vital);
                    if (!values.TryGetValue(canonical, out float current))
                        return false;

                    mutations.Add((vital, delta));
                    values[canonical] = current + delta;
                    return true;
                });
            return player;
        }

        private static Mock<ICharacterSpell> CreateCharacterSpell()
        {
            var characterSpell = new Mock<ICharacterSpell>();
            characterSpell.SetupGet(spell => spell.MaxAbilityCharges).Returns(1u);
            characterSpell.SetupGet(spell => spell.AbilityCharges).Returns(1u);
            return characterSpell;
        }

        private static SpellParameters CreateParameters(
            CastMethod castMethod,
            Spell4Entry entry,
            ICharacterSpell characterSpell = null,
            uint globalCooldown = 0u)
        {
            var baseInfo = new Mock<ISpellBaseInfo>();
            baseInfo.SetupGet(info => info.Entry).Returns(new Spell4BaseEntry
            {
                CastMethod = (uint)castMethod
            });
            var spellInfo = new Mock<ISpellInfo>();
            spellInfo.SetupGet(info => info.Entry).Returns(entry);
            spellInfo.SetupGet(info => info.BaseInfo).Returns(baseInfo.Object);
            spellInfo.SetupGet(info => info.GlobalCooldown).Returns(globalCooldown == 0u
                ? null
                : new SpellCoolDownEntry { CooldownTime = globalCooldown });
            spellInfo.SetupGet(info => info.Effects).Returns([]);
            spellInfo.SetupGet(info => info.Telegraphs).Returns([]);
            spellInfo.SetupGet(info => info.PrerequisiteRunners).Returns([]);
            return new SpellParameters
            {
                SpellInfo      = spellInfo.Object,
                CharacterSpell = characterSpell
            };
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

        private sealed class TestSpell : NexusForever.Game.Spell.Spell
        {
            public int TargetSelectionCount { get; private set; }

            public TestSpell(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            public CastResult CheckCastForTest()
            {
                return CheckCast();
            }

            public void ExecuteForTest(bool consumeVitalCost = true)
            {
                Execute(consumeVitalCost);
            }

            public void SetStatus(SpellStatus value)
            {
                status = value;
            }

            protected override void SelectTargets()
            {
                TargetSelectionCount++;
            }
        }

        private sealed class TestPendingUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public int PendingSpellCount => GetPendingSpells().Count;

            public TestPendingUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void AddPendingSpell(ISpell spell)
            {
                GetPendingSpells().Add(spell);
            }

            protected override float CalculateDefaultProperty(Property property)
            {
                return 0f;
            }

            protected override IEntityModel BuildEntityModel()
            {
                return new NonPlayerEntityModel();
            }

            private List<ISpell> GetPendingSpells()
            {
                FieldInfo field = typeof(UnitEntity).GetField(
                    "pendingSpells",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return (List<ISpell>)field.GetValue(this);
            }
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
        private delegate bool TryGetVitalMaximum(Vital vital, out float maximum);
    }
}
