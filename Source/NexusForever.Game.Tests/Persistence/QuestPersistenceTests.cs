using System.Collections.Immutable;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable.Model;
using NexusForever.Script;
using NexusForever.Shared;
using QuestEntity = NexusForever.Game.Quest.Quest;

namespace NexusForever.Game.Tests.Persistence
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class QuestPersistenceCollection
    {
        public const string Name = "Quest persistence";
    }

    [Collection(QuestPersistenceCollection.Name)]
    public sealed class QuestPersistenceTests : IDisposable
    {
        private const ulong CharacterId = 42ul;
        private const ushort QuestId = 100;
        private const uint ObjectiveId = 200u;

        private readonly IServiceProvider originalServiceProvider;
        private readonly ServiceProvider serviceProvider;

        public QuestPersistenceTests()
        {
            originalServiceProvider = LegacyServiceProvider.Provider;
            serviceProvider = new ServiceCollection()
                .AddSingleton(new Mock<IScriptManager>().Object)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        [Fact]
        public void Save_WithoutAcknowledgementRetainsQuestAndObjectiveChanges()
        {
            QuestEntity quest = CreateLoadedQuest();
            IQuestObjective objective = Assert.Single(quest);
            quest.Flags = QuestStateFlags.Tracked;
            objective.Progress = 3u;
            objective.Timer = 750u;

            using (TestCharacterContext context = CreateContext())
                quest.Save(context, new SaveCommitScope());

            using TestCharacterContext retryContext = CreateContext();
            quest.Save(retryContext, new SaveCommitScope());

            CharacterQuestModel questModel = Assert.Single(retryContext.ChangeTracker.Entries<CharacterQuestModel>()).Entity;
            Assert.Equal((byte)QuestStateFlags.Tracked, questModel.Flags);
            CharacterQuestObjectiveModel objectiveModel = Assert.Single(
                retryContext.ChangeTracker.Entries<CharacterQuestObjectiveModel>()).Entity;
            Assert.Equal(3u, objectiveModel.Progress);
            Assert.Equal(750u, objectiveModel.Timer);
        }

        [Fact]
        public void Save_SuccessfulAcknowledgementClearsUnchangedQuestAndObjectiveChanges()
        {
            QuestEntity quest = CreateLoadedQuest();
            IQuestObjective objective = Assert.Single(quest);
            quest.Timer = 1_000u;
            objective.Progress = 4u;
            var scope = new SaveCommitScope();

            using (TestCharacterContext context = CreateContext())
                quest.Save(context, scope);
            scope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext retryContext = CreateContext();
            quest.Save(retryContext, new SaveCommitScope());

            Assert.Empty(retryContext.ChangeTracker.Entries<CharacterQuestModel>());
            Assert.Empty(retryContext.ChangeTracker.Entries<CharacterQuestObjectiveModel>());
        }

        [Fact]
        public void Save_MutationsDuringPendingCommitRemainDirtyAfterAcknowledgement()
        {
            QuestEntity quest = CreateLoadedQuest();
            IQuestObjective objective = Assert.Single(quest);
            quest.Timer = 1_000u;
            objective.Progress = 4u;
            objective.Timer = 500u;
            var scope = new SaveCommitScope();

            using (TestCharacterContext context = CreateContext())
                quest.Save(context, scope);

            quest.Timer = 2_000u;
            objective.Progress = 7u;
            objective.Timer = 250u;
            scope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext retryContext = CreateContext();
            quest.Save(retryContext, new SaveCommitScope());

            CharacterQuestModel questModel = Assert.Single(retryContext.ChangeTracker.Entries<CharacterQuestModel>()).Entity;
            Assert.Equal(2_000u, questModel.Timer);
            CharacterQuestObjectiveModel objectiveModel = Assert.Single(
                retryContext.ChangeTracker.Entries<CharacterQuestObjectiveModel>()).Entity;
            Assert.Equal(7u, objectiveModel.Progress);
            Assert.Equal(250u, objectiveModel.Timer);
        }

        [Fact]
        public void Save_LoadedQuestAndObjectiveAreClean()
        {
            QuestEntity quest = CreateLoadedQuest();

            using TestCharacterContext context = CreateContext();
            quest.Save(context, new SaveCommitScope());

            Assert.Empty(context.ChangeTracker.Entries<CharacterQuestModel>());
            Assert.Empty(context.ChangeTracker.Entries<CharacterQuestObjectiveModel>());
        }

        [Fact]
        public void Save_ParentDeleteDoesNotStageObjectiveChanges()
        {
            QuestEntity quest = CreateLoadedQuest();
            Assert.Single(quest).Progress = 9u;
            quest.EnqueueDelete(true);

            using TestCharacterContext context = CreateContext();
            quest.Save(context, new SaveCommitScope());

            Assert.Equal(EntityState.Deleted, Assert.Single(
                context.ChangeTracker.Entries<CharacterQuestModel>()).State);
            Assert.Empty(context.ChangeTracker.Entries<CharacterQuestObjectiveModel>());
        }

        [Fact]
        public void Save_DeleteCancellationAfterStagingQueuesParentAndObjectiveCreates()
        {
            QuestEntity quest = CreateLoadedQuest();
            quest.EnqueueDelete(true);
            var scope = new SaveCommitScope();

            using (TestCharacterContext context = CreateContext())
                quest.Save(context, scope);

            quest.EnqueueDelete(false);
            scope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext retryContext = CreateContext();
            quest.Save(retryContext, new SaveCommitScope());

            Assert.Equal(EntityState.Added, Assert.Single(
                retryContext.ChangeTracker.Entries<CharacterQuestModel>()).State);
            var objectiveEntry = Assert.Single(
                retryContext.ChangeTracker.Entries<CharacterQuestObjectiveModel>());
            Assert.Equal(EntityState.Added, objectiveEntry.State);
            Assert.Equal(1_000u, objectiveEntry.Entity.Timer);
        }

        [Fact]
        public void Save_CreateAndDeleteBeforeFirstSaveStagesNoDatabaseChanges()
        {
            QuestEntity quest = CreateNewQuest();
            quest.EnqueueDelete(true);
            bool deleteAcknowledged = false;
            var scope = new SaveCommitScope();

            using TestCharacterContext context = CreateContext();
            quest.Save(context, scope, () => deleteAcknowledged = true);

            Assert.Empty(context.ChangeTracker.Entries<CharacterQuestModel>());
            Assert.Empty(context.ChangeTracker.Entries<CharacterQuestObjectiveModel>());
            Assert.False(deleteAcknowledged);

            scope.CreateAcknowledgement().Acknowledge();
            Assert.True(deleteAcknowledged);
        }

        [Fact]
        public void QuestManager_DeleteRemainsRetryableUntilCommitAcknowledgement()
        {
            QuestEntity quest = CreateLoadedQuest();
            quest.EnqueueDelete(true);
            IQuestManager manager = CreateQuestManager(quest);

            using (TestCharacterContext context = CreateContext())
                manager.Save(context, new SaveCommitScope());
            Assert.Same(quest, Assert.Single(manager.GetActiveQuests()));

            var retryScope = new SaveCommitScope();
            using (TestCharacterContext retryContext = CreateContext())
            {
                manager.Save(retryContext, retryScope);
                Assert.Equal(EntityState.Deleted, Assert.Single(
                    retryContext.ChangeTracker.Entries<CharacterQuestModel>()).State);
            }

            retryScope.CreateAcknowledgement().Acknowledge();
            Assert.Empty(manager.GetActiveQuests());

            using TestCharacterContext acknowledgedContext = CreateContext();
            manager.Save(acknowledgedContext, new SaveCommitScope());
            Assert.Empty(acknowledgedContext.ChangeTracker.Entries<CharacterQuestModel>());
        }

        [Fact]
        public void QuestManager_DeleteCancellationAfterStagingKeepsQuestAndQueuesCreates()
        {
            QuestEntity quest = CreateLoadedQuest();
            quest.EnqueueDelete(true);
            IQuestManager manager = CreateQuestManager(quest);
            var scope = new SaveCommitScope();

            using (TestCharacterContext context = CreateContext())
                manager.Save(context, scope);

            quest.EnqueueDelete(false);
            scope.CreateAcknowledgement().Acknowledge();

            Assert.Same(quest, Assert.Single(manager.GetActiveQuests()));
            using TestCharacterContext retryContext = CreateContext();
            manager.Save(retryContext, new SaveCommitScope());

            Assert.Equal(EntityState.Added, Assert.Single(
                retryContext.ChangeTracker.Entries<CharacterQuestModel>()).State);
            Assert.Equal(EntityState.Added, Assert.Single(
                retryContext.ChangeTracker.Entries<CharacterQuestObjectiveModel>()).State);
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = originalServiceProvider;
            serviceProvider.Dispose();
        }

        private static QuestEntity CreateLoadedQuest()
        {
            IQuestInfo questInfo = CreateQuestInfo().Quest;
            var model = new CharacterQuestModel
            {
                Id      = CharacterId,
                QuestId = QuestId,
                State   = (byte)QuestState.Accepted
            };
            model.QuestObjective.Add(new CharacterQuestObjectiveModel
            {
                Id       = CharacterId,
                QuestId  = QuestId,
                Index    = 0,
                Progress = 1u,
                Timer    = 1_000u
            });

            return new QuestEntity(CreatePlayer(), questInfo, model);
        }

        private static QuestEntity CreateNewQuest()
        {
            IQuestInfo questInfo = CreateQuestInfo().Quest;
            return new QuestEntity(CreatePlayer(), questInfo);
        }

        private static IQuestManager CreateQuestManager(IQuest quest)
        {
            var manager = new QuestManager(CreatePlayer(), new CharacterModel
            {
                Id = CharacterId
            });
            FieldInfo field = typeof(QuestManager).GetField("activeQuests", BindingFlags.Instance | BindingFlags.NonPublic);
            var activeQuests = (Dictionary<ushort, IQuest>)field.GetValue(manager);
            activeQuests.Add(quest.Id, quest);
            return manager;
        }

        private static IPlayer CreatePlayer()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);
            return player.Object;
        }

        private static (IQuestInfo Quest, IQuestObjectiveInfo Objective) CreateQuestInfo()
        {
            var objectiveEntry = new QuestObjectiveEntry
            {
                Id    = ObjectiveId,
                Type  = (uint)QuestObjectiveType.CollectItem,
                Data  = 300u,
                Count = 10u
            };
            var objective = new Mock<IQuestObjectiveInfo>();
            objective.SetupGet(value => value.Id).Returns(ObjectiveId);
            objective.SetupGet(value => value.Type).Returns(QuestObjectiveType.CollectItem);
            objective.SetupGet(value => value.Entry).Returns(objectiveEntry);

            var quest = new Mock<IQuestInfo>();
            quest.SetupGet(value => value.Entry).Returns(new Quest2Entry
            {
                Id = QuestId
            });
            quest.SetupGet(value => value.Objectives).Returns(ImmutableList.Create(objective.Object));
            return (quest.Object, objective.Object);
        }

        private static TestCharacterContext CreateContext()
        {
            return new TestCharacterContext();
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
