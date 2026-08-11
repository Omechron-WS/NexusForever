using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Achievement;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Entity.Movement.Generator;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Abstract.Reputation;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.CSI;
using NexusForever.Game.Entity;
using NexusForever.Game.Loot;
using NexusForever.Game.Quest;
using NexusForever.Game.Spell;
using NexusForever.Game.Spell.SpellType;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reputation;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Combat;
using NexusForever.GameTable;
using NexusForever.GameTable.Configuration.Model;
using NexusForever.GameTable.Model;
using NexusForever.Network.Session;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Script;
using NexusForever.Script.Main.AI;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using QuestEntity = NexusForever.Game.Quest.Quest;

namespace NexusForever.Game.Tests.Quest
{
    /// <summary>
    /// Deterministic server-domain coverage for the build-16042 Crimson Isle quest 5593 loop.
    /// This deliberately does not assert that optional external world SQL has been deployed.
    /// </summary>
    [Collection(CombatServiceProviderCollection.Name)]
    public sealed class CrimsonIsleQuest5593IntegrationTests : IDisposable
    {
        private const ulong CharacterId = 42ul;
        private const ushort QuestId = 5_593;
        private const uint MineObjectiveId = 8_247u;
        private const uint ScrabObjectiveId = 8_248u;
        private const uint MineCreatureId = 24_251u;
        private const uint ScrabCreatureId = 24_054u;
        private const uint MondoCreatureId = 24_187u;
        private const uint MineSpellId = 42_303u;
        private const uint MineSpellBaseId = 26_494u;
        private const uint MineSpellEffectId = 95_973u;
        private const uint ScrabAttackSpellId = 65_785u;
        private const ushort MediumArmourRewardId = 3_890;
        private const uint MediumArmourItemId = 12_657u;
        private const uint InitialExperience = 1_710u;
        private const uint QuestExperience = 152u;
        private const uint QuestCredits = 83u;

        private readonly IServiceProvider previousProvider;
        private readonly ImmutableDictionary<InventoryLocation, uint> previousInventoryCapacities;
        private readonly ServiceProvider serviceProvider;
        private readonly GameTableManager gameTableManager;
        private readonly ItemManager itemManager;
        private readonly AssetManager assetManager;
        private readonly Mock<IScriptManager> scriptManager = new();
        private readonly Mock<IAssetManager> objectiveAssetManager = new();
        private readonly List<QuestManager> questManagers = [];

        public CrimsonIsleQuest5593IntegrationTests()
        {
            previousProvider = LegacyServiceProvider.Provider;
            previousInventoryCapacities = AssetManager.InventoryLocationCapacities;

            gameTableManager = CreateGameTableManager();
            itemManager = CreateItemManager();
            assetManager = CreateAssetManager();
            EntityManager entityManager = CreateEntityManager();
            GlobalSpellManager globalSpellManager = new();
            GlobalLootManager globalLootManager = CreateGlobalLootManager();

            var scripts = new Mock<IScriptCollection>();
            scriptManager
                .Setup(manager => manager.InitialiseOwnedScripts<IQuest>(
                    It.IsAny<IQuest>(), It.IsAny<uint>()))
                .Returns(scripts.Object);
            scriptManager
                .Setup(manager => manager.InitialiseOwnedScripts<ISpell>(
                    It.IsAny<ISpell>(), It.IsAny<uint>()))
                .Returns(scripts.Object);

            objectiveAssetManager
                .Setup(manager => manager.GetQuestObjectiveTargetIds(It.IsAny<uint>()))
                .Returns(ImmutableList<uint>.Empty);
            objectiveAssetManager
                .Setup(manager => manager.GetTargetGroupsForCreatureId(It.IsAny<uint>()))
                .Returns(ImmutableList<uint>.Empty);

            SetInventoryCapacities(ImmutableDictionary<InventoryLocation, uint>.Empty
                .Add(InventoryLocation.Inventory, 8u));

            serviceProvider = new ServiceCollection()
                .AddSingleton<IScriptManager>(scriptManager.Object)
                .AddSingleton(gameTableManager)
                .AddSingleton(itemManager)
                .AddSingleton(assetManager)
                .AddSingleton(entityManager)
                .AddSingleton(globalSpellManager)
                .AddSingleton(globalLootManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;

            RegisterQuestAdvanceObjectiveHandler(globalSpellManager);
        }

        [Fact]
        public void Build16042Loop_UsesRealRuntimeSeamsAndSurvivesPersistenceReload()
        {
            var map = new Mock<IBaseMap>();
            map.Setup(world => world.ScheduleRespawn(It.IsAny<IWorldEntity>())).Returns(true);

            QuestInfo questInfo = new(gameTableManager.Quest2.GetEntry(QuestId));
            AssertBuild16042Contract(questInfo);

            Mock<IGlobalQuestManager> globalQuestManager = CreateGlobalQuestManager(questInfo);
            PlayerRuntime runtime = CreateRuntime(CreateCharacterModel(), questInfo, globalQuestManager, map.Object);

            runtime.QuestManager.QuestAdd(QuestId, null);
            Assert.Equal(QuestState.Accepted, runtime.QuestManager.GetQuestState(QuestId));

            AssertScrabCombatProfileCastsBuildSpell(runtime, map.Object);

            IQuest activeQuest = Assert.Single(runtime.QuestManager.GetActiveQuests());
            IQuestObjective mineObjective = GetObjective(activeQuest, MineObjectiveId);
            for (uint activation = 1u; activation <= 3u; activation++)
            {
                RunMineInteraction(runtime, map.Object, 100u + activation);
                Assert.Equal(activation, mineObjective.Progress);
            }

            Assert.Equal(QuestState.Accepted, activeQuest.State);

            IQuestObjective scrabObjective = GetObjective(activeQuest, ScrabObjectiveId);
            uint[] expectedProgress = [250u, 500u, 750u, 1_000u];
            for (int kill = 0; kill < expectedProgress.Length; kill++)
            {
                var scrab = new QuestCreditUnitEntity(new Mock<IMovementManager>().Object);
                scrab.AttachToMap(map.Object, 200u + (uint)kill, 2_000u + (uint)kill, ScrabCreatureId);
                scrab.SetMaximumAndCurrentHealth(100u);
                scrab.AddRewardParticipant(runtime.Player.Object);

                scrab.ModifyHealth(100u, DamageType.Physical, runtime.Player.Object);
                scrab.ModifyHealth(100u, DamageType.Physical, runtime.Player.Object);

                Assert.Equal(expectedProgress[kill], scrabObjective.Progress);
            }

            Assert.Equal(QuestState.Achieved, activeQuest.State);
            Assert.Empty(GetInventoryItems(runtime.Inventory));
            Assert.Empty(runtime.CurrencyManager);
            Assert.Equal(InitialExperience, runtime.XpManager.TotalXp);

            runtime.QuestManager.QuestComplete(QuestId, 0, false);
            runtime.QuestManager.QuestComplete(QuestId, (ushort)MediumArmourItemId, false);
            runtime.QuestManager.QuestComplete(QuestId, 3_888, false);
            runtime.QuestManager.QuestComplete(QuestId, 3_889, false);

            Assert.Equal(QuestState.Achieved, activeQuest.State);
            Assert.Empty(GetInventoryItems(runtime.Inventory));
            Assert.Empty(runtime.CurrencyManager);
            Assert.Equal(InitialExperience, runtime.XpManager.TotalXp);

            runtime.QuestManager.QuestComplete(QuestId, MediumArmourRewardId, false);

            Assert.Equal(QuestState.Completed, runtime.QuestManager.GetQuestState(QuestId));
            runtime.Player.Verify(
                player => player.GetVisibleCreature<WorldEntity>(MondoCreatureId),
                Times.AtLeast(2));
            IItem rewardItem = Assert.Single(GetInventoryItems(runtime.Inventory));
            Assert.Equal(MediumArmourItemId, rewardItem.Id);
            Assert.Equal(1u, rewardItem.StackCount);
            Assert.Equal((ulong)QuestCredits, Assert.Single(runtime.CurrencyManager).Amount);
            Assert.Equal(InitialExperience + QuestExperience, runtime.XpManager.TotalXp);

            Assert.Throws<QuestException>(() =>
                runtime.QuestManager.QuestComplete(QuestId, MediumArmourRewardId, false));
            Assert.Single(GetInventoryItems(runtime.Inventory));
            Assert.Equal((ulong)QuestCredits, Assert.Single(runtime.CurrencyManager).Amount);
            Assert.Equal(InitialExperience + QuestExperience, runtime.XpManager.TotalXp);

            CharacterModel reloadedModel = StageAndClone(runtime);
            PlayerRuntime reloaded = CreateRuntime(reloadedModel, questInfo, globalQuestManager, map.Object);

            Assert.Equal(QuestState.Completed, reloaded.QuestManager.GetQuestState(QuestId));
            Assert.Empty(reloaded.QuestManager.GetActiveQuests());
            IItem reloadedReward = Assert.Single(GetInventoryItems(reloaded.Inventory));
            Assert.Equal(MediumArmourItemId, reloadedReward.Id);
            Assert.Equal(1u, reloadedReward.StackCount);
            Assert.Equal((ulong)QuestCredits, Assert.Single(reloaded.CurrencyManager).Amount);
            Assert.Equal(InitialExperience + QuestExperience, reloaded.XpManager.TotalXp);

            Assert.Throws<QuestException>(() =>
                reloaded.QuestManager.QuestComplete(QuestId, MediumArmourRewardId, false));
            Assert.Single(GetInventoryItems(reloaded.Inventory));
            Assert.Equal((ulong)QuestCredits, Assert.Single(reloaded.CurrencyManager).Amount);
            Assert.Equal(InitialExperience + QuestExperience, reloaded.XpManager.TotalXp);
        }

        public void Dispose()
        {
            foreach (QuestManager manager in questManagers)
                manager.Dispose();

            LegacyServiceProvider.Provider = previousProvider;
            SetInventoryCapacities(previousInventoryCapacities);
            serviceProvider.Dispose();
        }

        private void AssertBuild16042Contract(IQuestInfo questInfo)
        {
            Assert.Equal((uint)QuestId, questInfo.Entry.Id);
            Assert.Equal(12u, questInfo.Entry.Flags);
            Assert.Equal(3u, questInfo.Entry.ConLevel);
            Assert.Equal(1u, questInfo.Entry.QuestPlayerFactionEnum);
            Assert.Equal(new uint[] { MineObjectiveId, ScrabObjectiveId },
                questInfo.Entry.Objectives.Where(id => id != 0u));

            Assert.Collection(
                questInfo.Objectives,
                objective =>
                {
                    Assert.Equal(MineObjectiveId, objective.Id);
                    Assert.Equal(QuestObjectiveType.ActivateEntity, objective.Type);
                    Assert.Equal(MineCreatureId, objective.Entry.Data);
                    Assert.Equal(3u, objective.Entry.Count);
                    Assert.Equal(4u, objective.Entry.Flags);
                },
                objective =>
                {
                    Assert.Equal(ScrabObjectiveId, objective.Id);
                    Assert.Equal(QuestObjectiveType.KillCreature, objective.Type);
                    Assert.Equal(ScrabCreatureId, objective.Entry.Data);
                    Assert.Equal(4u, objective.Entry.Count);
                    Assert.Equal(4u, objective.Entry.Flags);
                });

            Assert.Equal(new uint[] { 3_888u, 3_889u, 3_890u }, questInfo.Rewards.Keys.Order());
            Assert.All(questInfo.Rewards.Values, reward =>
            {
                Assert.Equal(QuestRewardType.Item, (QuestRewardType)reward.Quest2RewardTypeId);
                Assert.Equal(1u, reward.ObjectAmount);
                Assert.Equal(1u, reward.Flags);
            });
            Assert.Equal(MediumArmourItemId, questInfo.Rewards[MediumArmourRewardId].ObjectId);
            Assert.Equal(QuestExperience, questInfo.GetRewardExperience());
            Assert.Equal(QuestCredits, questInfo.GetRewardMoney());

            Creature2Entry mine = gameTableManager.Creature2.GetEntry(MineCreatureId);
            Assert.Equal(MineSpellId, mine.Spell4IdActivate00);
            Assert.Equal(2_000u, mine.ActivateSpellCastTime);
            Assert.Equal(0f, mine.ActivateSpellMinRange);
            Assert.Equal(3f, mine.ActivateSpellMaxRange);

            Spell4Entry mineSpell = gameTableManager.Spell4.GetEntry(MineSpellId);
            Assert.Equal(MineSpellBaseId, mineSpell.Spell4BaseIdBaseSpell);
            Assert.Equal(0u, mineSpell.CastTime);
            Assert.Equal(0f, mineSpell.TargetMinRange);
            Assert.Equal(5f, mineSpell.TargetMaxRange);
        }

        private PlayerRuntime CreateRuntime(
            CharacterModel model,
            IQuestInfo questInfo,
            Mock<IGlobalQuestManager> globalQuestManager,
            IBaseMap map)
        {
            var session = new Mock<IGameSession>();
            var movement = new Mock<IMovementManager>();
            movement.Setup(manager => manager.GetPosition()).Returns(new Vector3(10f, 0f, 0f));
            movement.Setup(manager => manager.GetRotation()).Returns(Vector3.Zero);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);
            player.SetupGet(value => value.Guid).Returns(10u);
            player.SetupGet(value => value.Session).Returns(session.Object);
            player.SetupGet(value => value.InWorld).Returns(true);
            player.SetupGet(value => value.IsAlive).Returns(true);
            player.SetupGet(value => value.Map).Returns(map);
            player.SetupGet(value => value.Position).Returns(Vector3.Zero);
            player.SetupGet(value => value.MovementManager).Returns(movement.Object);
            player.SetupGet(value => value.Faction1).Returns(Faction.Dominion);
            player.SetupGet(value => value.Race).Returns(Race.Draken);
            player.SetupGet(value => value.Class).Returns(Class.Warrior);
            player.SetupProperty(value => value.Level, 3u);
            player.SetupGet(value => value.SignatureEnabled).Returns(false);
            player.SetupGet(value => value.IsLoading).Returns(false);
            player.SetupGet(value => value.SpellManager).Returns(Mock.Of<ISpellManager>());
            player.SetupGet(value => value.ThreatManager).Returns(Mock.Of<IThreatManager>());
            player.SetupGet(value => value.AchievementManager)
                .Returns(Mock.Of<ICharacterAchievementManager>());
            player.Setup(value => value.GetItemProficiencies()).Returns(ItemProficiency.MediumArmor);

            var mondo = new QuestCreditUnitEntity(new Mock<IMovementManager>().Object);
            mondo.AttachToMap(map, 50u, 17_803u, MondoCreatureId);
            player
                .Setup(value => value.GetVisibleCreature<WorldEntity>(MondoCreatureId))
                .Returns([mondo]);

            var inventory = new Inventory(player.Object, model);
            var currencyManager = new CurrencyManager(gameTableManager);
            currencyManager.Initialise(player.Object, model);
            var xpManager = new XpManager(player.Object, model, gameTableManager);
            var reputationManager = new Mock<IReputationManager>();

            player.SetupGet(value => value.Inventory).Returns(inventory);
            player.SetupGet(value => value.CurrencyManager).Returns(currencyManager);
            player.SetupGet(value => value.XpManager).Returns(xpManager);
            player.SetupGet(value => value.ReputationManager).Returns(reputationManager.Object);

            var factionManager = new Mock<IFactionManager>();
            var prerequisiteManager = new Mock<IPrerequisiteManager>();
            var rewardManager = new QuestRewardManager(
                player.Object,
                itemManager,
                gameTableManager,
                factionManager.Object,
                prerequisiteManager.Object);
            var disableManager = new Mock<IDisableManager>();

            var questManager = new QuestManager(
                player.Object,
                model,
                globalQuestManager.Object,
                rewardManager,
                disableManager.Object,
                itemManager: itemManager,
                prerequisiteManager: prerequisiteManager.Object,
                gameTableManager: gameTableManager,
                questFactory: info => new QuestEntity(
                    player.Object,
                    info,
                    globalQuestManager.Object,
                    scriptManager.Object,
                    objectiveAssetManager.Object));
            player.SetupGet(value => value.QuestManager).Returns(questManager);
            questManagers.Add(questManager);

            return new PlayerRuntime(
                player,
                inventory,
                currencyManager,
                xpManager,
                questManager);
        }

        private static Mock<IGlobalQuestManager> CreateGlobalQuestManager(IQuestInfo questInfo)
        {
            var manager = new Mock<IGlobalQuestManager>();
            manager.Setup(value => value.GetQuestInfo(QuestId)).Returns(questInfo);
            manager.Setup(value => value.GetQuestGivers(QuestId)).Returns([MondoCreatureId]);
            manager.Setup(value => value.GetQuestReceivers(QuestId)).Returns([MondoCreatureId]);
            manager.Setup(value => value.GetQuestCommunicatorMessages(It.IsAny<ushort>())).Returns([]);
            manager
                .Setup(value => value.GetQuestCommunicatorQuestStateTriggers(
                    It.IsAny<ushort>(), It.IsAny<QuestState>()))
                .Returns([]);
            return manager;
        }

        private void RunMineInteraction(PlayerRuntime runtime, IBaseMap map, uint targetGuid)
        {
            Creature2Entry mineEntry = gameTableManager.Creature2.GetEntry(MineCreatureId);
            var mine = new Mock<IUnitEntity>();
            mine.SetupGet(value => value.Guid).Returns(targetGuid);
            mine.SetupGet(value => value.CreatureId).Returns(MineCreatureId);
            mine.SetupGet(value => value.CreatureEntry).Returns(mineEntry);
            mine.SetupGet(value => value.InWorld).Returns(true);
            mine.SetupGet(value => value.Map).Returns(map);
            mine.SetupGet(value => value.Position).Returns(Vector3.Zero);
            runtime.Player
                .Setup(value => value.GetVisible<IWorldEntity>(targetGuid))
                .Returns(mine.Object);

            var interaction = new ClientSideInteraction(
                runtime.Player.Object,
                mine.Object,
                targetGuid,
                assetManager: objectiveAssetManager.Object);
            SpellParameters parameters = CreateMineSpellParameters(interaction, mineEntry);
            parameters.PrimaryTargetId = targetGuid;
            var spell = new SpellClientSideInteraction(runtime.Player.Object, parameters);
            IQuest quest = Assert.Single(runtime.QuestManager.GetActiveQuests());
            uint progressBefore = GetObjective(quest, MineObjectiveId).Progress;

            spell.Cast();
            Assert.Equal(mineEntry.ActivateSpellCastTime, spell.Parameters.CastTimeOverride);
            spell.Update(1.999d);

            Assert.Equal(progressBefore, GetObjective(quest, MineObjectiveId).Progress);
            mine.Verify(value => value.OnActivateSuccess(runtime.Player.Object), Times.Never);

            spell.Update(0.002d);

            Assert.Equal(progressBefore + 1u, GetObjective(quest, MineObjectiveId).Progress);
            Assert.False(spell.SucceedClientInteraction());
            Assert.False(interaction.CompleteSuccess());
            mine.Verify(value => value.OnActivateSuccess(runtime.Player.Object), Times.Once);
        }

        private SpellParameters CreateMineSpellParameters(
            IClientSideInteraction interaction,
            Creature2Entry mineEntry)
        {
            var baseInfo = new Mock<ISpellBaseInfo>();
            baseInfo.SetupGet(value => value.Entry)
                .Returns(gameTableManager.Spell4Base.GetEntry(MineSpellBaseId));

            var spellInfo = new Mock<ISpellInfo>();
            spellInfo.SetupGet(value => value.Entry)
                .Returns(gameTableManager.Spell4.GetEntry(MineSpellId));
            spellInfo.SetupGet(value => value.BaseInfo).Returns(baseInfo.Object);
            spellInfo.SetupGet(value => value.Effects).Returns(
            [
                new Spell4EffectsEntry
                {
                    Id = MineSpellEffectId,
                    SpellId = MineSpellId,
                    EffectType = SpellEffectType.QuestAdvanceObjective,
                    TargetFlags = (uint)SpellEffectTargetFlags.Caster,
                    DataBits00 = QuestId,
                    DataBits01 = 0u,
                    DataBits02 = 1u,
                    PhaseFlags = uint.MaxValue
                }
            ]);
            spellInfo.SetupGet(value => value.Telegraphs).Returns([]);
            spellInfo.SetupGet(value => value.PrerequisiteRunners).Returns([]);

            return new SpellParameters
            {
                SpellInfo = spellInfo.Object,
                ClientSideInteraction = interaction,
                CastTimeOverride = mineEntry.ActivateSpellCastTime
            };
        }

        private static void AssertScrabCombatProfileCastsBuildSpell(PlayerRuntime runtime, IBaseMap map)
        {
            var owner = new Mock<ICreatureEntity>();
            var ownerMovement = new Mock<IMovementManager>();
            var directMovement = new Mock<IDirectMovementGenerator>();
            var threatManager = new Mock<IThreatManager>();
            var hostile = new Mock<IHostileEntity>();
            var loggerFactory = new Mock<ILoggerFactory>();
            uint? targetGuid = null;
            bool inWorld = false;
            var casts = new List<(uint SpellId, ISpellParameters Parameters)>();

            hostile.SetupGet(value => value.HatedUnitId).Returns(runtime.Player.Object.Guid);
            hostile.SetupGet(value => value.Threat).Returns(10u);
            hostile.SetupGet(value => value.IsExpired).Returns(false);
            threatManager.SetupGet(value => value.IsThreatened).Returns(true);
            threatManager.Setup(value => value.GetTopHostile()).Returns(hostile.Object);
            threatManager
                .Setup(value => value.GetHostile(runtime.Player.Object.Guid))
                .Returns(hostile.Object);

            ownerMovement.Setup(value => value.GetPosition()).Returns(Vector3.Zero);
            ownerMovement.Setup(value => value.GetRotation()).Returns(Vector3.Zero);
            loggerFactory
                .Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns(Mock.Of<ILogger>());

            owner.SetupGet(value => value.Guid).Returns(1u);
            owner.SetupGet(value => value.CreatureId).Returns(ScrabCreatureId);
            owner.SetupGet(value => value.Spline).Returns((NexusForever.Database.World.Model.EntitySplineModel)null);
            owner.SetupGet(value => value.Map).Returns(() => inWorld ? map : null);
            owner.SetupGet(value => value.InWorld).Returns(() => inWorld);
            owner.SetupGet(value => value.IsAlive).Returns(true);
            owner.SetupGet(value => value.TargetGuid).Returns(() => targetGuid);
            owner.SetupGet(value => value.LeashPosition).Returns(Vector3.Zero);
            owner.SetupGet(value => value.ThreatManager).Returns(threatManager.Object);
            owner.SetupGet(value => value.MovementManager).Returns(ownerMovement.Object);
            owner.Setup(value => value.GetPropertyValue(Property.MoveSpeedMultiplier)).Returns(1f);
            owner
                .Setup(value => value.GetVisible<IUnitEntity>(runtime.Player.Object.Guid))
                .Returns(runtime.Player.Object);
            owner.Setup(value => value.CanAttack(runtime.Player.Object)).Returns(true);
            owner
                .Setup(value => value.SetTarget(It.IsAny<IWorldEntity>(), It.IsAny<uint>()))
                .Callback<IWorldEntity, uint>((target, _) => targetGuid = target?.Guid);
            owner
                .Setup(value => value.GetActiveSpell(It.IsAny<Func<ISpell, bool>>()))
                .Returns((ISpell)null);
            owner
                .Setup(value => value.CastSpellTracked(It.IsAny<uint>(), It.IsAny<ISpellParameters>()))
                .Returns((uint spellId, ISpellParameters parameters) =>
                {
                    casts.Add((spellId, parameters));
                    return null;
                });

            var ai = new CombatAI(loggerFactory.Object, directMovement.Object);
            ai.OnLoad(owner.Object);
            inWorld = true;
            ai.OnAddToMap(map);
            ai.OnThreatAddTarget(hostile.Object);
            ai.Update(0.1d);
            ai.Update(1.5d);

            (uint spellId, ISpellParameters parameters) = Assert.Single(casts);
            Assert.Equal(ScrabAttackSpellId, spellId);
            Assert.Equal(runtime.Player.Object.Guid, parameters.PrimaryTargetId);
            ai.OnUnload();
        }

        private static CharacterModel StageAndClone(PlayerRuntime runtime)
        {
            using var context = new TestCharacterContext();
            CharacterModel stagedCharacter = context.Attach(new CharacterModel
            {
                Id = CharacterId,
                TotalXp = InitialExperience,
                RestBonusXp = 0u
            }).Entity;
            var commitScope = new SaveCommitScope();

            runtime.QuestManager.Save(context, commitScope);
            runtime.Inventory.Save(context, commitScope);
            runtime.CurrencyManager.Save(context, commitScope);
            runtime.XpManager.Save(context, commitScope);

            CharacterQuestModel stagedQuest = Assert.Single(
                context.ChangeTracker.Entries<CharacterQuestModel>()).Entity;
            Assert.Equal(QuestId, stagedQuest.QuestId);
            Assert.Equal((byte)QuestState.Completed, stagedQuest.State);

            CharacterQuestObjectiveModel[] stagedObjectives = context.ChangeTracker
                .Entries<CharacterQuestObjectiveModel>()
                .Select(entry => entry.Entity)
                .OrderBy(objective => objective.Index)
                .ToArray();
            Assert.Equal(new uint[] { 3u, 1_000u },
                stagedObjectives.Select(objective => objective.Progress));

            ItemModel stagedItem = Assert.Single(context.ChangeTracker.Entries<ItemModel>()).Entity;
            Assert.Equal(MediumArmourItemId, stagedItem.ItemId);
            Assert.Equal(1u, stagedItem.StackCount);

            CharacterCurrencyModel stagedCurrency = Assert.Single(
                context.ChangeTracker.Entries<CharacterCurrencyModel>()).Entity;
            Assert.Equal((byte)CurrencyType.Credits, stagedCurrency.CurrencyId);
            Assert.Equal((ulong)QuestCredits, stagedCurrency.Amount);
            Assert.Equal(InitialExperience + QuestExperience, stagedCharacter.TotalXp);
            Assert.True(context.Entry(stagedCharacter).Property(model => model.TotalXp).IsModified);

            CharacterModel clone = CloneCharacter(
                stagedCharacter,
                stagedQuest,
                stagedObjectives,
                stagedItem,
                stagedCurrency);
            commitScope.CreateAcknowledgement().Acknowledge();
            return clone;
        }

        private static CharacterModel CloneCharacter(
            CharacterModel character,
            CharacterQuestModel quest,
            IEnumerable<CharacterQuestObjectiveModel> objectives,
            ItemModel item,
            CharacterCurrencyModel currency)
        {
            var clone = new CharacterModel
            {
                Id = character.Id,
                TotalXp = character.TotalXp,
                RestBonusXp = character.RestBonusXp,
                LastOnline = null
            };
            var questClone = new CharacterQuestModel
            {
                Id = quest.Id,
                QuestId = quest.QuestId,
                State = quest.State,
                Flags = quest.Flags,
                Timer = quest.Timer,
                Reset = quest.Reset
            };
            foreach (CharacterQuestObjectiveModel objective in objectives)
            {
                questClone.QuestObjective.Add(new CharacterQuestObjectiveModel
                {
                    Id = objective.Id,
                    QuestId = objective.QuestId,
                    Index = objective.Index,
                    Progress = objective.Progress,
                    Timer = objective.Timer
                });
            }

            clone.Quest.Add(questClone);
            clone.Item.Add(new ItemModel
            {
                Id = item.Id,
                OwnerId = item.OwnerId,
                ItemId = item.ItemId,
                Location = item.Location,
                BagIndex = item.BagIndex,
                StackCount = item.StackCount,
                Charges = item.Charges,
                Durability = item.Durability,
                ExpirationTimeLeft = item.ExpirationTimeLeft
            });
            clone.Currency.Add(new CharacterCurrencyModel
            {
                Id = currency.Id,
                CurrencyId = currency.CurrencyId,
                Amount = currency.Amount
            });
            return clone;
        }

        private static IQuestObjective GetObjective(IQuest quest, uint objectiveId)
        {
            return quest.Single(objective => objective.ObjectiveInfo.Id == objectiveId);
        }

        private static List<IItem> GetInventoryItems(Inventory inventory)
        {
            return inventory.SelectMany(bag => bag).ToList();
        }

        private static CharacterModel CreateCharacterModel()
        {
            return new CharacterModel
            {
                Id = CharacterId,
                TotalXp = InitialExperience,
                RestBonusXp = 0u,
                LastOnline = null
            };
        }

        private static GameTableManager CreateGameTableManager()
        {
            var manager = new GameTableManager(Options.Create(new GameTableConfig()));
            SetGameTable(manager, nameof(GameTableManager.Quest2), CreateGameTable(new Quest2Entry
            {
                Id = QuestId,
                Flags = 12u,
                ConLevel = 3u,
                Type = 0u,
                PrerequisiteLevel = 1u,
                PrerequisiteQuests = [0u, 0u, 0u],
                QuestPlayerFactionEnum = 1u,
                WorldZoneId = 622u,
                PushedItemIds = [0u, 0u, 0u, 0u, 0u, 0u],
                PushedItemCounts = [0u, 0u, 0u, 0u, 0u, 0u],
                Objectives = [MineObjectiveId, ScrabObjectiveId, 0u, 0u, 0u, 0u],
                WorldLocation2IdReceiver = 17_803u,
                Quest2DifficultyId = 3u,
                QuestShareEnum = 1u
            }));
            SetGameTable(manager, nameof(GameTableManager.QuestObjective), CreateGameTable(
                new QuestObjectiveEntry
                {
                    Id = MineObjectiveId,
                    Type = (uint)QuestObjectiveType.ActivateEntity,
                    Flags = 4u,
                    Data = MineCreatureId,
                    Count = 3u,
                    WorldLocationsIdIndicator00 = 17_820u,
                    WorldLocationsIdIndicator01 = 17_821u,
                    TargetGroupIdRewardPane = 4_508u
                },
                new QuestObjectiveEntry
                {
                    Id = ScrabObjectiveId,
                    Type = (uint)QuestObjectiveType.KillCreature,
                    Flags = 4u,
                    Data = ScrabCreatureId,
                    Count = 4u,
                    WorldLocationsIdIndicator00 = 39_038u,
                    WorldLocationsIdIndicator01 = 39_039u
                }));
            SetGameTable(manager, nameof(GameTableManager.Quest2Difficulty), CreateGameTable(
                new Quest2DifficultyEntry
                {
                    Id = 3u,
                    XpMultiplier = 1f,
                    CashRewardMultiplier = 10f
                }));
            SetGameTable(manager, nameof(GameTableManager.Quest2Reward), CreateGameTable(
                CreateReward(3_888u, 27_917u),
                CreateReward(3_889u, 27_918u),
                CreateReward(MediumArmourRewardId, MediumArmourItemId)));
            SetGameTable(manager, nameof(GameTableManager.XpPerLevel), CreateGameTable(
                new XpPerLevelEntry
                {
                    Id = 3u,
                    MinXpForLevel = InitialExperience,
                    BaseQuestXpPerLevel = QuestExperience
                },
                new XpPerLevelEntry
                {
                    Id = 4u,
                    MinXpForLevel = 2_000u,
                    BaseQuestXpPerLevel = 228u
                }));
            SetGameTable(manager, nameof(GameTableManager.GameFormula), CreateGameTable(
                new GameFormulaEntry
                {
                    Id = 530u,
                    Datafloat0 = 1.9299999475f
                },
                new GameFormulaEntry
                {
                    Id = 655u,
                    Dataint0 = 40u
                }));
            SetGameTable(manager, nameof(GameTableManager.CurrencyType), CreateGameTable(
                new CurrencyTypeEntry
                {
                    Id = (uint)CurrencyType.Credits,
                    CapAmount = 0ul
                }));
            SetGameTable(manager, nameof(GameTableManager.Creature2), CreateGameTable(
                new Creature2Entry
                {
                    Id = ScrabCreatureId
                },
                new Creature2Entry
                {
                    Id = MineCreatureId,
                    Spell4IdActivate00 = MineSpellId,
                    ActivateSpellCastTime = 2_000u,
                    ActivateSpellMinRange = 0f,
                    ActivateSpellMaxRange = 3f
                }));
            SetGameTable(manager, nameof(GameTableManager.Spell4), CreateGameTable(
                new Spell4Entry
                {
                    Id = MineSpellId,
                    Spell4BaseIdBaseSpell = MineSpellBaseId,
                    TierIndex = 1u,
                    CastTime = 0u,
                    TargetMinRange = 0f,
                    TargetMaxRange = 5f,
                    TargetVerticalRange = -1f
                }));
            SetGameTable(manager, nameof(GameTableManager.Spell4Base), CreateGameTable(
                new Spell4BaseEntry
                {
                    Id = MineSpellBaseId,
                    Spell4HitResultId = 8u,
                    Spell4TargetMechanicId = 5u,
                    Spell4TargetAngleId = 1u,
                    Spell4PrerequisiteId = 1u,
                    Spell4ValidTargetId = 6u,
                    Spell4SpellTypesIdSpellType = 4u,
                    CastMethod = (uint)CastMethod.Normal,
                    SpellClass = 37u,
                    CastBarType = 1u
                }));
            return manager;
        }

        private static Quest2RewardEntry CreateReward(uint rewardId, uint itemId)
        {
            return new Quest2RewardEntry
            {
                Id = rewardId,
                Quest2Id = QuestId,
                Quest2RewardTypeId = (uint)QuestRewardType.Item,
                ObjectId = itemId,
                ObjectAmount = 1u,
                Flags = 1u
            };
        }

        private static ItemManager CreateItemManager()
        {
            var manager = new ItemManager();
            IItemInfo[] items =
            [
                CreateItemInfo(27_917u, 2u),
                CreateItemInfo(27_918u, 3u),
                CreateItemInfo(MediumArmourItemId, (uint)ItemProficiency.MediumArmor)
            ];
            SetPrivateField(manager, "item", items.ToImmutableDictionary(item => item.Id));
            SetPrivateField(manager, "equippedItemSlots",
                ImmutableDictionary<ItemSlot, ImmutableList<EquippedItem>>.Empty);
            SetPrivateField(manager, "nextItemId", 9_000ul);
            return manager;
        }

        private static IItemInfo CreateItemInfo(uint itemId, uint proficiency)
        {
            var info = new Mock<IItemInfo>();
            info.SetupGet(value => value.Id).Returns(itemId);
            info.SetupGet(value => value.Entry).Returns(new Item2Entry
            {
                Id = itemId,
                MaxStackCount = 1u
            });
            info.SetupGet(value => value.CategoryEntry).Returns(new Item2CategoryEntry
            {
                ItemProficiencyId = proficiency
            });
            info.Setup(value => value.IsStackable()).Returns(false);
            info.Setup(value => value.IsEquippable()).Returns(false);
            info.Setup(value => value.IsEquippableBag()).Returns(false);
            return info.Object;
        }

        private static AssetManager CreateAssetManager()
        {
            var manager = new AssetManager();
            SetPrivateField(manager, "creatureAssociatedTargetGroups",
                ImmutableDictionary<uint, ImmutableList<uint>>.Empty);
            SetPrivateField(manager, "questObjectiveTargets",
                ImmutableDictionary<uint, ImmutableList<uint>>.Empty);
            return manager;
        }

        private static EntityManager CreateEntityManager()
        {
            var manager = new EntityManager();
            typeof(EntityManager)
                .GetMethod("InitialiseEntityStats", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, null);
            return manager;
        }

        private static GlobalLootManager CreateGlobalLootManager()
        {
            var provider = new Mock<ILootTableProvider>();
            provider.Setup(value => value.LoadLootTables()).Returns(new LootTableData([], [], []));
            var manager = new GlobalLootManager(
                provider.Object,
                static () => 1d,
                static (_, _) => 0);
            manager.Initialise();
            return manager;
        }

        private static void RegisterQuestAdvanceObjectiveHandler(GlobalSpellManager manager)
        {
            FieldInfo field = typeof(GlobalSpellManager).GetField(
                "spellEffectDelegates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var handlers = (Dictionary<SpellEffectType, SpellEffectDelegate>)field.GetValue(manager);
            handlers[SpellEffectType.QuestAdvanceObjective] = SpellHandler.HandleEffectQuestAdvanceObjective;
        }

        private static void SetInventoryCapacities(
            ImmutableDictionary<InventoryLocation, uint> capacities)
        {
            PropertyInfo property = typeof(AssetManager).GetProperty(
                nameof(AssetManager.InventoryLocationCapacities),
                BindingFlags.Static | BindingFlags.Public);
            property.SetValue(null, capacities);
        }

        private static void SetGameTable<T>(GameTableManager manager, string propertyName, GameTable<T> table)
            where T : class, new()
        {
            typeof(GameTableManager).GetProperty(propertyName)?.SetValue(manager, table);
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int index = 0; index < entries.Length; index++)
                lookup[(uint)idField.GetValue(entries[index])] = index;

            typeof(GameTable<T>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<T>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader
                {
                    MaxId = maximumId + 1ul
                });
            return table;
        }

        private static void SetPrivateField<T>(object owner, string fieldName, T value)
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(owner, value);
        }

        private sealed record PlayerRuntime(
            Mock<IPlayer> Player,
            Inventory Inventory,
            CurrencyManager CurrencyManager,
            XpManager XpManager,
            QuestManager QuestManager);

        private sealed class QuestCreditUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public QuestCreditUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void AttachToMap(IBaseMap map, uint guid, uint entityId, uint creatureId)
            {
                PropertyInfo mapProperty = typeof(GridEntity).GetProperty(nameof(Map));
                mapProperty.GetSetMethod(true).Invoke(this, [map]);
                typeof(WorldEntity)
                    .GetProperty(nameof(CreatureEntry))
                    .GetSetMethod(true)
                    .Invoke(this, [new Creature2Entry { Id = creatureId }]);
                Guid = guid;
                Position = Vector3.Zero;
                EntityId = entityId;
            }

            public void SetMaximumAndCurrentHealth(uint health)
            {
                MaxHealth = health;
                Health = health;
            }

            public void AddRewardParticipant(IPlayer player)
            {
                AddVisible(player);
                ThreatManager.UpdateThreat(player, 1);
            }

            protected override float CalculateDefaultProperty(Property property)
            {
                return 0f;
            }

            protected override IEntityModel BuildEntityModel()
            {
                return new NonPlayerEntityModel();
            }
        }

        private sealed class TestCharacterContext : CharacterContext
        {
            public TestCharacterContext()
                : base(new DbContextOptionsBuilder<CharacterContext>()
                    .UseMySql(
                        "Server=localhost;Database=nexus_forever_test;User=test;Password=test;",
                        new MySqlServerVersion(new Version(8, 0, 36)))
                    .Options)
            {
            }
        }
    }
}
