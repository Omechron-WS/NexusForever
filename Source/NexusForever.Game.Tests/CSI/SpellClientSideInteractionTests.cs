using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.CSI;
using NexusForever.Game.Entity;
using NexusForever.Game.Quest;
using NexusForever.Game.Spell;
using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Script;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using Moq;
using QuestEntity = NexusForever.Game.Quest.Quest;

namespace NexusForever.Game.Tests.CSI
{
    [Collection(CombatServiceProviderCollection.Name)]
    public sealed class SpellClientSideInteractionTests : IDisposable
    {
        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public SpellClientSideInteractionTests()
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
        public void ResolveCastMethod_InteractionForcesCsiSpellType()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(entry: null);
            SpellParameters parameters = CreateParameters(CastMethod.Normal, interaction.Object);

            Assert.Equal(CastMethod.ClientSideInteraction, UnitEntity.ResolveCastMethod(parameters));

            parameters.ClientSideInteraction = null;
            Assert.Equal(CastMethod.Normal, UnitEntity.ResolveCastMethod(parameters));
        }

        [Fact]
        public void ProxyCast_UsesDedicatedStartAndAutoSucceedsAfterOverride()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(entry: null);
            Mock<IPlayer> player = CreatePlayer(out Mock<IGameSession> session);
            SpellParameters parameters = CreateParameters(CastMethod.Normal, interaction.Object);
            parameters.CastTimeOverride = 2_000u;
            parameters.PrimaryTargetId = 55u;
            var spell = new TestClientSideInteractionSpell(player.Object, parameters);

            spell.Cast();

            session.Verify(value => value.EnqueueMessageEncrypted(
                It.Is<ServerSpellStartClientInteraction>(message =>
                    message.ClientUniqueId == 99u
                    && message.CastingId == spell.CastingId
                    && message.CasterId == 55u)), Times.Once);
            Assert.True(spell.SucceedClientInteraction());
            Assert.Equal(0, spell.ExecutionCount);
            spell.Update(1.9d);
            interaction.Verify(value => value.CompleteSuccess(), Times.Never);

            spell.Update(0.2d);

            Assert.Equal(1, spell.ExecutionCount);
            interaction.Verify(value => value.CompleteSuccess(), Times.Once);
        }

        [Fact]
        public void Cast_CommitsTypedGlobalCooldownOnceBeforeInteractionStart()
        {
            var lifecycle = new List<string>();
            Mock<IClientSideInteraction> interaction = CreateInteraction(entry: null);
            Mock<IPlayer> player = CreatePlayer(out Mock<IGameSession> session);
            Mock<ISpellManager> spellManager = Mock.Get(player.Object.SpellManager);
            spellManager.Setup(manager => manager.SetGlobalSpellCooldown(3u, 1.5d))
                .Callback(() => lifecycle.Add("cooldown"));
            session.Setup(value => value.EnqueueMessageEncrypted(
                    It.IsAny<ServerSpellStartClientInteraction>()))
                .Callback(() => lifecycle.Add("start"));
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                interaction.Object,
                new Spell4Entry
                {
                    Id                 = 123u,
                    GlobalCooldownEnum = 3u
                });
            Mock.Get(parameters.SpellInfo).SetupGet(value => value.GlobalCooldown)
                .Returns(new SpellCoolDownEntry { CooldownTime = 1500u });
            var spell = new TestClientSideInteractionSpell(player.Object, parameters);

            spell.Cast();
            Assert.True(spell.SucceedClientInteraction());

            Assert.Equal(["cooldown", "start"], lifecycle);
            spellManager.Verify(manager => manager.GetGlobalSpellCooldown(3u), Times.Once);
            spellManager.Verify(manager => manager.SetGlobalSpellCooldown(3u, 1.5d), Times.Once);
        }

        [Fact]
        public void Cast_ActiveTypedGlobalCooldownFailsWithoutCommitOrInteractionStart()
        {
            var packets = new List<IWritable>();
            Mock<IClientSideInteraction> interaction = CreateInteraction(entry: null);
            Mock<IPlayer> player = CreatePlayer(out Mock<IGameSession> session, isLoading: false);
            session.Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(packets.Add);
            Mock<ISpellManager> spellManager = Mock.Get(player.Object.SpellManager);
            spellManager.Setup(manager => manager.GetGlobalSpellCooldown(3u)).Returns(1d);
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                interaction.Object,
                new Spell4Entry
                {
                    Id                 = 123u,
                    GlobalCooldownEnum = 3u
                });
            var spell = new TestClientSideInteractionSpell(player.Object, parameters);

            spell.Cast();

            Assert.True(spell.IsFinishing);
            ServerSpellCastResult result = Assert.IsType<ServerSpellCastResult>(Assert.Single(packets));
            Assert.Equal(CastResult.SpellGlobalCooldown, result.CastResult);
            spellManager.Verify(manager => manager.GetGlobalSpellCooldown(3u), Times.Once);
            spellManager.Verify(
                manager => manager.SetGlobalSpellCooldown(It.IsAny<uint>(), It.IsAny<double>()),
                Times.Never);
            session.Verify(value => value.EnqueueMessageEncrypted(
                It.IsAny<ServerSpellStartClientInteraction>()), Times.Never);
            interaction.Verify(value => value.TriggerFail(), Times.Once);
        }

        [Fact]
        public void ProxyCast_DedicatedStartPublishesOneFinishFromLateUpdate()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(entry: null);
            Mock<IPlayer> player = CreatePlayer(out _);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.Normal, interaction.Object));
            spell.Cast();

            spell.Finish();

            player.Verify(value => value.EnqueueToVisible(
                It.IsAny<ServerSpellFinish>(), true), Times.Never);

            spell.LateUpdate(0d);
            spell.LateUpdate(0d);
            spell.Dispose();

            player.Verify(value => value.EnqueueToVisible(
                It.Is<ServerSpellFinish>(message => message.ServerUniqueId == spell.CastingId),
                true), Times.Once);
        }

        [Theory]
        [InlineData(0u, 0u, 65d)]
        [InlineData(1_000u, 2_500u, 8.5d)]
        [InlineData(uint.MaxValue, 7_500u, 72.5d)]
        [InlineData(400_000u, 0u, 305d)]
        public void NativeCast_UsesGenericStartAndBoundedResultTimeout(
            uint duration,
            uint castTime,
            double timeout)
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry
            {
                Duration = duration
            });
            Mock<IPlayer> player = CreatePlayer(out Mock<IGameSession> session);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object));
            spell.Parameters.PrimaryTargetId = 55u;
            spell.Parameters.CastTimeOverride = castTime;

            spell.Cast();

            player.Verify(value => value.EnqueueToVisible(
                It.Is<ServerSpellStart>(message =>
                    message.PrimaryTargetId == player.Object.Guid
                    && message.InitialPositionData.Count == 1
                    && message.InitialPositionData[0].UnitId == player.Object.Guid),
                true), Times.Once);
            session.Verify(value => value.EnqueueMessageEncrypted(
                It.IsAny<ServerSpellStartClientInteraction>()), Times.Never);
            spell.Update(timeout - 0.1d);
            interaction.Verify(value => value.TriggerFail(), Times.Never);

            spell.Update(0.2d);
            spell.LateUpdate(0d);

            Assert.True(spell.IsFinished);
            interaction.Verify(value => value.TriggerFail(), Times.Once);
        }

        [Theory]
        [InlineData(CastMethod.Normal, true)]
        [InlineData(CastMethod.ClientSideInteraction, false)]
        public void StartPacketSelection_DependsOnCsiEntryInsteadOfOriginalCastMethod(
            CastMethod castMethod,
            bool hasEntry)
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(
                hasEntry ? new ClientSideInteractionEntry() : null);
            Mock<IPlayer> player = CreatePlayer(out Mock<IGameSession> session);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(castMethod, interaction.Object));

            spell.Cast();

            player.Verify(value => value.EnqueueToVisible(It.IsAny<ServerSpellStart>(), true),
                hasEntry ? Times.Once : Times.Never);
            session.Verify(value => value.EnqueueMessageEncrypted(
                    It.IsAny<ServerSpellStartClientInteraction>()),
                hasEntry ? Times.Never : Times.Once);
            Assert.Equal(hasEntry, spell.RequiresClientResult);

            spell.Finish();
        }

        [Fact]
        public void NonNativeCastWithInteractionEntry_WaitsForClientResult()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            Mock<IPlayer> player = CreatePlayer(out _);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.Normal, interaction.Object));

            spell.Cast();
            spell.Update(10d);

            Assert.Equal(0, spell.ExecutionCount);
            interaction.Verify(value => value.CompleteSuccess(), Times.Never);

            Assert.True(spell.SucceedClientInteraction());
            Assert.Equal(1, spell.ExecutionCount);
            interaction.Verify(value => value.CompleteSuccess(), Times.Once);
        }

        [Fact]
        public void SuccessResult_IsSingleUseAndExecutesOnce()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            Mock<IPlayer> player = CreatePlayer(out _);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object));
            spell.Cast();

            Assert.True(spell.SucceedClientInteraction());
            Assert.False(spell.SucceedClientInteraction());
            Assert.False(spell.FailClientInteraction());

            Assert.Equal(1, spell.ExecutionCount);
            interaction.Verify(value => value.CompleteSuccess(), Times.Once);
            interaction.Verify(value => value.TriggerFail(), Times.Never);
        }

        [Fact]
        public void NativeEarlySuccess_WaitsForAuthoritativeCastTime()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry
            {
                Duration = 10_000u
            });
            Mock<IPlayer> player = CreatePlayer(out _);
            SpellParameters parameters = CreateParameters(CastMethod.ClientSideInteraction, interaction.Object);
            parameters.CastTimeOverride = 2_000u;
            var spell = new TestClientSideInteractionSpell(player.Object, parameters);
            spell.Cast();

            Assert.True(spell.SucceedClientInteraction());
            Assert.Equal(0, spell.ExecutionCount);
            spell.Update(1.9d);
            Assert.Equal(0, spell.ExecutionCount);

            spell.Update(0.2d);

            Assert.Equal(1, spell.ExecutionCount);
            interaction.Verify(value => value.CompleteSuccess(), Times.Once);
        }

        [Fact]
        public void NativeFailureBeforeCastReady_CancelsWithoutExecution()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            Mock<IPlayer> player = CreatePlayer(out _);
            SpellParameters parameters = CreateParameters(CastMethod.ClientSideInteraction, interaction.Object);
            parameters.CastTimeOverride = 2_000u;
            var spell = new TestClientSideInteractionSpell(player.Object, parameters);
            spell.Cast();

            Assert.True(spell.FailClientInteraction());
            spell.Update(3d);

            Assert.Equal(0, spell.ExecutionCount);
            interaction.Verify(value => value.TriggerFail(), Times.Once);
            interaction.Verify(value => value.CompleteSuccess(), Times.Never);
        }

        [Fact]
        public void SuccessResult_InvalidBoundaryFailsWithoutExecuting()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry(), isValid: false);
            Mock<IPlayer> player = CreatePlayer(out _);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object));
            spell.Cast();

            Assert.True(spell.SucceedClientInteraction());

            Assert.Equal(0, spell.ExecutionCount);
            interaction.Verify(value => value.TriggerFail(), Times.Once);
            interaction.Verify(value => value.CompleteSuccess(), Times.Never);
        }

        [Fact]
        public void SuccessResult_ValidatesOnceBeforeExecutionAndCannotBeReclassifiedAfterEffects()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            interaction.SetupSequence(value => value.IsValid())
                .Returns(true)
                .Returns(false);
            Mock<IPlayer> player = CreatePlayer(out _);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object));
            spell.Cast();

            Assert.True(spell.SucceedClientInteraction());

            Assert.Equal(1, spell.ExecutionCount);
            interaction.Verify(value => value.IsValid(), Times.Once);
            interaction.Verify(value => value.CompleteSuccess(), Times.Once);
            interaction.Verify(value => value.TriggerFail(), Times.Never);
        }

        [Fact]
        public void ProxyClientCancel_DoesNotInvokeFailureCallbackAndRejectsReplay()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            Mock<IPlayer> player = CreatePlayer(out _);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.Normal, interaction.Object));
            spell.Parameters.CastTimeOverride = 2_000u;
            spell.Cast();

            Assert.True(spell.CancelClientInteraction());
            Assert.False(spell.CancelClientInteraction());
            Assert.False(spell.SucceedClientInteraction());

            interaction.Verify(value => value.TriggerFail(), Times.Never);
            interaction.Verify(value => value.CompleteSuccess(), Times.Never);
        }

        [Fact]
        public void StartPacketFailure_FailsInteractionAndCleansUp()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(entry: null);
            Mock<IPlayer> player = CreatePlayer(out Mock<IGameSession> session);
            session.Setup(value => value.EnqueueMessageEncrypted(
                    It.IsAny<ServerSpellStartClientInteraction>()))
                .Throws(new InvalidOperationException("Test send failure."));
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.Normal, interaction.Object));

            spell.Cast();
            spell.LateUpdate(0d);

            Assert.True(spell.IsFinished);
            interaction.Verify(value => value.TriggerFail(), Times.Once);
            interaction.Verify(value => value.CompleteSuccess(), Times.Never);
        }

        [Fact]
        public void FailureCallbackException_DoesNotLeaveInteractionCasting()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            interaction.Setup(value => value.TriggerFail())
                .Throws(new InvalidOperationException("Test callback failure."));
            Mock<IPlayer> player = CreatePlayer(out _);
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object));
            spell.Cast();

            Assert.True(spell.FailClientInteraction());
            spell.LateUpdate(0d);

            Assert.True(spell.IsFinished);
        }

        [Fact]
        public void CastValidationException_FailsAndBecomesCleanupEligible()
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            Mock<IPlayer> player = CreatePlayer(out _);
            Mock.Get(player.Object.SpellManager)
                .Setup(manager => manager.GetSpellCooldown(It.IsAny<uint>()))
                .Throws(new InvalidOperationException("Test validation failure."));
            var spell = new TestClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object));

            Exception exception = Record.Exception(spell.Cast);
            spell.LateUpdate(0d);

            Assert.Null(exception);
            Assert.True(spell.IsFinished);
            interaction.Verify(value => value.TriggerFail(), Times.Once);
        }

        [Fact]
        public void ExecutionException_SendsResultAndCancellationThenCleansUp()
        {
            var packets = new List<IWritable>();
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            Mock<IPlayer> player = CreatePlayer(out Mock<IGameSession> session, isLoading: false);
            session.Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(packets.Add);
            var spell = new ThrowingClientSideInteractionSpell(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object));
            spell.Cast();

            Assert.True(spell.SucceedClientInteraction());

            Assert.True(spell.IsFinishing);
            Assert.Collection(
                packets,
                packet => Assert.Equal(CastResult.SpellBad, Assert.IsType<ServerSpellCastResult>(packet).CastResult),
                packet =>
                {
                    Server07F9 cancel = Assert.IsType<Server07F9>(packet);
                    Assert.Equal(CastResult.SpellBad, cancel.CastResult);
                    Assert.True(cancel.CancelCast);
                });
            interaction.Verify(value => value.TriggerFail(), Times.Once);
            interaction.Verify(value => value.CompleteSuccess(), Times.Never);
        }

        [Fact]
        public void VitalDrainedBeforeSuccess_FailsWithoutCostOrSuccessCallback()
        {
            float resource = 50f;
            var packets = new List<IWritable>();
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            Mock<IPlayer> player = CreatePlayer(out Mock<IGameSession> session, isLoading: false);
            session.Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(packets.Add);
            player.Setup(unit => unit.TryGetVitalValue(
                    Vital.Resource1,
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital _, out float value) =>
                {
                    value = resource;
                    return true;
                }));
            var entry = new Spell4Entry
            {
                Id              = 123u,
                InnateCostType0 = (uint)Vital.Resource1,
                InnateCost0     = 50u
            };
            var spell = new SpellClientSideInteraction(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object, entry));
            spell.Cast();
            resource = 49f;

            Assert.True(spell.SucceedClientInteraction());

            Assert.True(spell.IsFinishing);
            player.Verify(unit => unit.TryModifyVital(
                It.IsAny<Vital>(), It.IsAny<float>(), It.IsAny<IUnitEntity>()), Times.Never);
            Assert.Collection(
                packets,
                packet => Assert.Equal(
                    CastResult.CasterVitalCostResource1,
                    Assert.IsType<ServerSpellCastResult>(packet).CastResult),
                packet => Assert.Equal(
                    CastResult.CasterVitalCostResource1,
                    Assert.IsType<Server07F9>(packet).CastResult));
            interaction.Verify(value => value.TriggerFail(), Times.Once);
            interaction.Verify(value => value.CompleteSuccess(), Times.Never);
        }

        [Fact]
        public void SuccessfulInteraction_ConsumesVitalCostExactlyOnce()
        {
            float resource = 50f;
            Mock<IClientSideInteraction> interaction = CreateInteraction(new ClientSideInteractionEntry());
            Mock<IPlayer> player = CreatePlayer(out _);
            player.Setup(unit => unit.TryGetVitalValue(
                    Vital.Resource1,
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital _, out float value) =>
                {
                    value = resource;
                    return true;
                }));
            player.Setup(unit => unit.TryModifyVital(
                    Vital.Resource1,
                    It.IsAny<float>(),
                    It.IsAny<IUnitEntity>()))
                .Returns((Vital _, float delta, IUnitEntity _) =>
                {
                    resource += delta;
                    return true;
                });
            var entry = new Spell4Entry
            {
                Id              = 123u,
                InnateCostType0 = (uint)Vital.Resource1,
                InnateCost0     = 50u
            };
            var spell = new SpellClientSideInteraction(
                player.Object,
                CreateParameters(CastMethod.ClientSideInteraction, interaction.Object, entry));
            spell.Cast();

            Assert.True(spell.SucceedClientInteraction());
            Assert.False(spell.SucceedClientInteraction());

            Assert.Equal(0f, resource);
            player.Verify(unit => unit.TryModifyVital(
                Vital.Resource1,
                -50f,
                It.IsAny<IUnitEntity>()), Times.Once);
            interaction.Verify(value => value.CompleteSuccess(), Times.Once);
            interaction.Verify(value => value.TriggerFail(), Times.Never);
        }

        [Fact]
        public void ProxyCast_Q5593ImmediateAdvanceSuppressesOnlyDuplicateActivationCredit()
        {
            RegisterQuestAdvanceObjectiveHandler();

            var map = Mock.Of<NexusForever.Game.Abstract.Map.IBaseMap>();
            Mock<IPlayer> player = CreatePlayer(out _);
            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.InWorld).Returns(true);
            player.SetupGet(value => value.Map).Returns(map);

            var activateUnit = new Mock<IUnitEntity>();
            activateUnit.SetupGet(value => value.Guid).Returns(100u);
            activateUnit.SetupGet(value => value.CreatureId).Returns(24_251u);
            activateUnit.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                Id                    = 24_251u,
                ActivateSpellMinRange = 0f,
                ActivateSpellMaxRange = 5f
            });
            activateUnit.SetupGet(value => value.InWorld).Returns(true);
            activateUnit.SetupGet(value => value.Map).Returns(map);
            player.Setup(value => value.GetVisible<IWorldEntity>(100u))
                .Returns(activateUnit.Object);

            var assetManager = new Mock<IAssetManager>();
            assetManager
                .Setup(value => value.GetQuestObjectiveTargetIds(8_247u))
                .Returns(ImmutableList<uint>.Empty);
            assetManager
                .Setup(value => value.GetTargetGroupsForCreatureId(24_251u))
                .Returns(ImmutableList<uint>.Empty);

            var objectiveInfo = new QuestObjectiveInfo(new QuestObjectiveEntry
            {
                Id    = 8_247u,
                Type  = (uint)QuestObjectiveType.ActivateEntity,
                Data  = 24_251u,
                Count = 3u
            });
            var questInfo = new Mock<IQuestInfo>();
            questInfo.SetupGet(value => value.Entry).Returns(new Quest2Entry
            {
                Id               = 5_593u,
                PushedItemIds    = [],
                PushedItemCounts = []
            });
            questInfo.SetupGet(value => value.Objectives)
                .Returns(ImmutableList.Create<IQuestObjectiveInfo>(objectiveInfo));

            var globalQuestManager = new Mock<IGlobalQuestManager>();
            var scriptManager = serviceProvider.GetRequiredService<IScriptManager>();
            var quest = new QuestEntity(
                player.Object,
                questInfo.Object,
                globalQuestManager.Object,
                scriptManager,
                assetManager.Object);
            var questManager = new QuestManager(
                player.Object,
                new CharacterModel(),
                globalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>());
            GetActiveQuests(questManager).Add(quest.Id, quest);
            player.SetupGet(value => value.QuestManager).Returns(questManager);

            var interaction = new ClientSideInteraction(
                player.Object,
                activateUnit.Object,
                42u,
                assetManager: assetManager.Object);
            SpellParameters parameters = CreateParameters(
                CastMethod.Normal,
                interaction,
                new Spell4Entry
                {
                    Id       = 42_303u,
                    CastTime = 0u
                });
            parameters.PrimaryTargetId = activateUnit.Object.Guid;
            Mock.Get(parameters.SpellInfo).SetupGet(value => value.Effects).Returns(
            [
                new Spell4EffectsEntry
                {
                    Id          = 95_973u,
                    SpellId     = 42_303u,
                    EffectType  = SpellEffectType.QuestAdvanceObjective,
                    TargetFlags = (uint)SpellEffectTargetFlags.Caster,
                    DataBits00  = 5_593u,
                    DataBits01  = 0u,
                    DataBits02  = 1u,
                    PhaseFlags  = uint.MaxValue
                }
            ]);
            var spell = new SpellClientSideInteraction(player.Object, parameters);

            spell.Cast();
            spell.Update(0d);

            IQuestObjective objective = Assert.Single(quest);
            Assert.Equal(1u, objective.Progress);
            Assert.False(spell.SucceedClientInteraction());
            Assert.False(spell.CancelClientInteraction());
            Assert.False(interaction.CompleteSuccess());
            Assert.Equal(1u, objective.Progress);
            activateUnit.Verify(value => value.OnActivateSuccess(player.Object), Times.Once);
        }

        [Theory]
        [InlineData(0u, false)]
        [InlineData(1u, true)]
        [InlineData(5_000u, true)]
        public void MovementInterruption_UsesEffectiveActivationCastTime(uint castTimeOverride, bool expected)
        {
            Mock<IClientSideInteraction> interaction = CreateInteraction(entry: null);
            Mock<IPlayer> player = CreatePlayer(out _);
            SpellParameters parameters = CreateParameters(CastMethod.Normal, interaction.Object);
            parameters.CastTimeOverride = castTimeOverride;
            var spell = new TestClientSideInteractionSpell(player.Object, parameters);

            Assert.Equal(expected, spell.IsMovingInterrupted());
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        private static Mock<IClientSideInteraction> CreateInteraction(
            ClientSideInteractionEntry entry,
            bool isValid = true)
        {
            var interaction = new Mock<IClientSideInteraction>();
            interaction.SetupGet(value => value.ClientUniqueId).Returns(99u);
            interaction.SetupGet(value => value.Entry).Returns(entry);
            interaction.Setup(value => value.IsValid()).Returns(isValid);
            interaction.Setup(value => value.CompleteSuccess()).Returns(true);
            interaction.Setup(value => value.TriggerFail()).Returns(true);
            return interaction;
        }

        private static Mock<IPlayer> CreatePlayer(out Mock<IGameSession> session, bool isLoading = true)
        {
            session = new Mock<IGameSession>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.IsLoading).Returns(isLoading);
            player.SetupGet(value => value.IsAlive).Returns(true);
            player.SetupGet(value => value.Guid).Returns(10u);
            player.SetupGet(value => value.Session).Returns(session.Object);
            player.SetupGet(value => value.SpellManager).Returns(Mock.Of<ISpellManager>());
            return player;
        }

        private static SpellParameters CreateParameters(
            CastMethod castMethod,
            IClientSideInteraction interaction,
            Spell4Entry entry = null)
        {
            var baseInfo = new Mock<ISpellBaseInfo>();
            baseInfo.SetupGet(value => value.Entry).Returns(new Spell4BaseEntry
            {
                CastMethod = (uint)castMethod
            });
            var spellInfo = new Mock<ISpellInfo>();
            spellInfo.SetupGet(value => value.Entry).Returns(entry ?? new Spell4Entry
            {
                Id = 123u
            });
            spellInfo.SetupGet(value => value.BaseInfo).Returns(baseInfo.Object);
            spellInfo.SetupGet(value => value.Effects).Returns([]);
            spellInfo.SetupGet(value => value.Telegraphs).Returns([]);
            spellInfo.SetupGet(value => value.PrerequisiteRunners).Returns([]);
            return new SpellParameters
            {
                SpellInfo = spellInfo.Object,
                ClientSideInteraction = interaction
            };
        }

        private void RegisterQuestAdvanceObjectiveHandler()
        {
            GlobalSpellManager globalSpellManager = serviceProvider.GetRequiredService<GlobalSpellManager>();
            FieldInfo field = typeof(GlobalSpellManager).GetField(
                "spellEffectDelegates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var handlers = (Dictionary<SpellEffectType, SpellEffectDelegate>)field.GetValue(globalSpellManager);
            handlers[SpellEffectType.QuestAdvanceObjective] = SpellHandler.HandleEffectQuestAdvanceObjective;
        }

        private static Dictionary<ushort, IQuest> GetActiveQuests(QuestManager manager)
        {
            FieldInfo field = typeof(QuestManager).GetField(
                "activeQuests",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (Dictionary<ushort, IQuest>)field.GetValue(manager);
        }

        private sealed class TestClientSideInteractionSpell : SpellClientSideInteraction
        {
            public int ExecutionCount { get; private set; }

            public TestClientSideInteractionSpell(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            protected override void Execute(bool consumeVitalCost = true)
            {
                ExecutionCount++;
                status = SpellStatus.Executing;
            }
        }

        private sealed class ThrowingClientSideInteractionSpell : SpellClientSideInteraction
        {
            public ThrowingClientSideInteractionSpell(IUnitEntity caster, ISpellParameters parameters)
                : base(caster, parameters)
            {
            }

            protected override void Execute(bool consumeVitalCost = true)
            {
                status = SpellStatus.Executing;
                throw new InvalidOperationException("Test execution failure.");
            }
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
    }
}
