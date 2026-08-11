using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Database;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Entity;
using NexusForever.Game.Loot;
using NexusForever.Game.Quest;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Tests.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Configuration.Model;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Script;
using NexusForever.Shared;
using QuestEntity = NexusForever.Game.Quest.Quest;

namespace NexusForever.Game.Tests.Quest
{
    [Collection(VitalServiceProviderCollection.Name)]
    public sealed class KillDifficultyObjectiveAdapterTests : IDisposable
    {
        private const uint CreatureId = 100u;
        private const ushort QuestId = 9_940;
        private const uint ObjectiveId = 18_972u;

        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public KillDifficultyObjectiveAdapterTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var gameTableManager = new GameTableManager(Options.Create(new GameTableConfig()));
            SetGameTable(
                gameTableManager,
                nameof(GameTableManager.Creature2Difficulty),
                CreateGameTable(
                    new Creature2DifficultyEntry { Id = 5u },
                    new Creature2DifficultyEntry { Id = 6u },
                    new Creature2DifficultyEntry { Id = 7u },
                    new Creature2DifficultyEntry { Id = 24u }));

            var entityManager = new EntityManager();
            typeof(EntityManager)
                .GetMethod("InitialiseEntityStats", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entityManager, null);

            var assetManager = new AssetManager();
            SetPrivateField(
                assetManager,
                "creatureAssociatedTargetGroups",
                ImmutableDictionary<uint, ImmutableList<uint>>.Empty);

            var lootTableProvider = new Mock<ILootTableProvider>();
            lootTableProvider
                .Setup(provider => provider.LoadLootTables())
                .Returns(new LootTableData([], [], []));
            var globalLootManager = new GlobalLootManager(
                lootTableProvider.Object,
                static () => 1d,
                static (_, _) => 0);
            globalLootManager.Initialise();

            serviceProvider = new ServiceCollection()
                .AddSingleton(gameTableManager)
                .AddSingleton(entityManager)
                .AddSingleton(assetManager)
                .AddSingleton(globalLootManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        [Fact]
        public void UnitDeath_ReportsValidatedDifficultyOnceToEachVisibleThreatParticipant()
        {
            DifficultyKillUnitEntity entity = CreateEntity(7u);
            PlayerParticipant first = CreateParticipant(10u, 100ul);
            PlayerParticipant second = CreateParticipant(20u, 200ul);
            PlayerParticipant hidden = CreateParticipant(30u, 300ul);
            entity.AddRewardParticipant(first.Player.Object, true);
            entity.AddRewardParticipant(second.Player.Object, true);
            entity.AddRewardParticipant(hidden.Player.Object, false);

            entity.Kill();
            entity.Kill();

            VerifyKillCredit(first.QuestManager, 7u);
            VerifyKillCredit(second.QuestManager, 7u);
            hidden.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(8u)]
        public void UnitDeath_InvalidDifficultyFailsClosedAndPreservesCreatureCredit(uint difficultyId)
        {
            DifficultyKillUnitEntity entity = CreateEntity(difficultyId);
            PlayerParticipant participant = CreateParticipant(10u, 100ul);
            entity.AddRewardParticipant(participant.Player.Object, true);

            entity.Kill();

            participant.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.KillCreature,
                    CreatureId,
                    1u),
                Times.Once);
            participant.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.KillCreature2,
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
            participant.QuestManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Once);
        }

        [Fact]
        public void IsTarget_UsesInclusiveDifficultyThresholdOnlyForKillCreature2()
        {
            QuestObjective difficulty = CreateObjective(QuestObjectiveType.KillCreature2, 6u);
            QuestObjective missingThreshold = CreateObjective(QuestObjectiveType.KillCreature2, 0u);
            QuestObjective ordinary = CreateObjective(QuestObjectiveType.KillCreature, 6u);

            Assert.False(difficulty.IsTarget(5u));
            Assert.True(difficulty.IsTarget(6u));
            Assert.True(difficulty.IsTarget(7u));
            Assert.True(difficulty.IsTarget(24u));
            Assert.False(missingThreshold.IsTarget(0u));
            Assert.False(missingThreshold.IsTarget(uint.MaxValue));
            Assert.True(ordinary.IsTarget(6u));
            Assert.False(ordinary.IsTarget(7u));
        }

        [Fact]
        public void QuestObjectiveUpdate_EmitsRawProgressPacketsAtInclusiveDifficultyBoundary()
        {
            QuestFixture fixture = CreateQuest();

            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.KillCreature2, 5u, 1u);
            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.KillCreature2, 6u, 1u);
            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.KillCreature2, 24u, 1u);
            for (int i = 0; i < 18; i++)
                fixture.Quest.ObjectiveUpdate(QuestObjectiveType.KillCreature2, 6u, 1u);

            IReadOnlyList<ServerQuestObjectiveUpdate> messages =
                GetMessages<ServerQuestObjectiveUpdate>(fixture.Session);
            Assert.Equal(20u, Assert.Single(fixture.Quest).Progress);
            Assert.Equal(QuestState.Achieved, fixture.Quest.State);
            Assert.Equal(
                Enumerable.Range(1, 20).Select(progress => (uint)progress),
                messages.Select(message => message.Completed));
            Assert.All(messages, message =>
            {
                Assert.Equal(QuestId, message.QuestId);
                Assert.Equal(0u, message.QuestObjectiveIndex);
            });

            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.KillCreature2, 24u, 1u);

            Assert.Equal(20, GetMessages<ServerQuestObjectiveUpdate>(fixture.Session).Count);
        }

        private static DifficultyKillUnitEntity CreateEntity(uint difficultyId)
        {
            var entity = new DifficultyKillUnitEntity(Mock.Of<IMovementManager>());
            entity.Attach(1u, CreatureId, difficultyId);
            entity.SetMaximumAndCurrentHealth(100u);
            return entity;
        }

        private static PlayerParticipant CreateParticipant(uint guid, ulong characterId)
        {
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Guid).Returns(guid);
            player.SetupGet(value => value.CharacterId).Returns(characterId);
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            player.SetupGet(value => value.ThreatManager).Returns(Mock.Of<IThreatManager>());
            player.SetupGet(value => value.Session).Returns(Mock.Of<IGameSession>());
            return new PlayerParticipant(player, questManager);
        }

        private static void VerifyKillCredit(Mock<IQuestManager> questManager, uint difficultyId)
        {
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.KillCreature,
                    CreatureId,
                    1u),
                Times.Once);
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.KillCreature2,
                    difficultyId,
                    1u),
                Times.Once);
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Exactly(2));
        }

        private static QuestObjective CreateObjective(QuestObjectiveType type, uint data)
        {
            IQuestObjectiveInfo objectiveInfo = CreateObjectiveInfo(type, data, 1u);
            var assetManager = new Mock<IAssetManager>();
            assetManager
                .Setup(manager => manager.GetQuestObjectiveTargetIds(It.IsAny<uint>()))
                .Returns(ImmutableList<uint>.Empty);
            return new QuestObjective(
                Mock.Of<IPlayer>(),
                CreateQuestInfo(objectiveInfo),
                objectiveInfo,
                (byte)0,
                assetManager.Object);
        }

        private static QuestFixture CreateQuest()
        {
            IQuestObjectiveInfo objectiveInfo = CreateObjectiveInfo(
                QuestObjectiveType.KillCreature2,
                6u,
                20u,
                (QuestObjectiveFlags)0x0600);
            IQuestInfo questInfo = CreateQuestInfo(objectiveInfo);

            var session = new Mock<IGameSession>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.Session).Returns(session.Object);
            player.SetupGet(value => value.QuestManager).Returns(Mock.Of<IQuestManager>());

            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager
                .Setup(manager => manager.GetQuestCommunicatorQuestStateTriggers(
                    It.IsAny<ushort>(),
                    It.IsAny<QuestState>()))
                .Returns([]);

            var assetManager = new Mock<IAssetManager>();
            assetManager
                .Setup(manager => manager.GetQuestObjectiveTargetIds(It.IsAny<uint>()))
                .Returns(ImmutableList<uint>.Empty);

            var quest = new QuestEntity(
                player.Object,
                questInfo,
                globalQuestManager.Object,
                Mock.Of<IScriptManager>(),
                assetManager.Object);
            return new QuestFixture(quest, session);
        }

        private static IQuestObjectiveInfo CreateObjectiveInfo(
            QuestObjectiveType type,
            uint data,
            uint count,
            QuestObjectiveFlags flags = QuestObjectiveFlags.None)
        {
            return new QuestObjectiveInfo(new QuestObjectiveEntry
            {
                Id    = ObjectiveId,
                Type  = (uint)type,
                Data  = data,
                Count = count,
                Flags = (uint)flags
            });
        }

        private static IQuestInfo CreateQuestInfo(params IQuestObjectiveInfo[] objectives)
        {
            var info = new Mock<IQuestInfo>();
            info.SetupGet(value => value.Entry).Returns(new Quest2Entry
            {
                Id               = QuestId,
                PushedItemIds    = [],
                PushedItemCounts = []
            });
            info.SetupGet(value => value.Objectives).Returns(objectives.ToImmutableList());
            return info.Object;
        }

        private static IReadOnlyList<T> GetMessages<T>(Mock<IGameSession> session) where T : IWritable
        {
            return session.Invocations
                .Where(invocation => invocation.Method.Name == nameof(IGameSession.EnqueueMessageEncrypted))
                .Select(invocation => invocation.Arguments[0])
                .OfType<T>()
                .ToArray();
        }

        private static void SetGameTable<T>(
            GameTableManager manager,
            string propertyName,
            GameTable<T> table)
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

        private sealed record PlayerParticipant(
            Mock<IPlayer> Player,
            Mock<IQuestManager> QuestManager);

        private sealed record QuestFixture(
            QuestEntity Quest,
            Mock<IGameSession> Session);

        private sealed class DifficultyKillUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public DifficultyKillUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void Attach(uint guid, uint creatureId, uint difficultyId)
            {
                typeof(WorldEntity)
                    .GetProperty(nameof(CreatureEntry))
                    .GetSetMethod(true)
                    .Invoke(this, [new Creature2Entry
                    {
                        Id                    = creatureId,
                        Creature2DifficultyId = difficultyId
                    }]);
                Guid = guid;
                Position = Vector3.Zero;
            }

            public void SetMaximumAndCurrentHealth(uint health)
            {
                MaxHealth = health;
                Health = health;
            }

            public void AddRewardParticipant(IPlayer player, bool visible)
            {
                if (visible)
                    AddVisible(player);

                ThreatManager.UpdateThreat(player, 1);
            }

            public void Kill()
            {
                ModifyHealth(100u, DamageType.Physical, null);
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
    }
}
