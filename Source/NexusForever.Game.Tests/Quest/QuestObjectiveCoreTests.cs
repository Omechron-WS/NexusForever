using System.Collections.Immutable;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Entity;
using NexusForever.Game.Quest;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Tests.Persistence;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Script;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using QuestEntity = NexusForever.Game.Quest.Quest;

namespace NexusForever.Game.Tests.Quest
{
    public sealed class QuestObjectiveChecklistTests
    {
        [Fact]
        public void ObjectiveUpdate_ChecklistUsesValidatedRawBitIndices()
        {
            QuestObjective objective = CreateObjective(QuestObjectiveType.ActivateTargetGroupChecklist, 3u);

            objective.ObjectiveUpdate(2u);
            objective.ObjectiveUpdate(2u);
            objective.ObjectiveUpdate(3u);
            objective.ObjectiveUpdate(255u);

            Assert.Equal(0x04u, objective.Progress);
            Assert.False(objective.IsComplete());

            objective.ObjectiveUpdate(0u);
            objective.ObjectiveUpdate(1u);

            Assert.Equal(0x07u, objective.Progress);
            Assert.True(objective.IsComplete());
        }

        [Fact]
        public void ObjectiveUpdate_ChecklistCountAboveStorageWidthCannotProgressOrComplete()
        {
            QuestObjective objective = CreateObjective(QuestObjectiveType.ActivateTargetGroupChecklist, 40u);

            objective.ObjectiveUpdate(0u);
            objective.Complete();

            Assert.Equal(0u, objective.Progress);
            Assert.False(objective.IsComplete());

            objective.Progress = uint.MaxValue;
            Assert.False(objective.IsComplete());
        }

        [Fact]
        public void ObjectiveUpdate_ZeroCountChecklistPreservesImmediateCompletion()
        {
            QuestObjective objective = CreateObjective(QuestObjectiveType.Unknown10, 0u);

            Assert.True(objective.IsComplete());

            objective.ObjectiveUpdate(0u);
            objective.ObjectiveUpdate(255u);
            objective.Complete();

            Assert.Equal(0u, objective.Progress);
            Assert.True(objective.IsComplete());
        }

        [Fact]
        public void Complete_ChecklistUsesExactLowBitMask()
        {
            QuestObjective objective = CreateObjective(QuestObjectiveType.ActivateTargetGroupChecklist, 3u);

            objective.Complete();

            Assert.Equal(0x07u, objective.Progress);
            Assert.True(objective.IsComplete());
        }

        [Fact]
        public void Complete_ThirtyTwoEntryChecklistUsesEntireStorageMask()
        {
            QuestObjective objective = CreateObjective(QuestObjectiveType.ActivateTargetGroupChecklist, 32u);

            objective.Complete();

            Assert.Equal(uint.MaxValue, objective.Progress);
            Assert.True(objective.IsComplete());
        }

        [Fact]
        public void IsTarget_MatchesDirectAndExpandedTargets()
        {
            IQuestObjectiveInfo objectiveInfo = CreateObjectiveInfo(
                1u,
                QuestObjectiveType.ActivateTargetGroupChecklist,
                2u,
                data: 500u);
            IQuestInfo questInfo = CreateQuestInfo(objectiveInfo);
            var assetManager = new Mock<IAssetManager>();
            assetManager.Setup(manager => manager.GetQuestObjectiveTargetIds(objectiveInfo.Id))
                .Returns([100u, 101u]);
            var objective = new QuestObjective(
                Mock.Of<IPlayer>(),
                questInfo,
                objectiveInfo,
                0,
                assetManager.Object);

            Assert.True(objective.IsTarget(500u));
            Assert.True(objective.IsTarget(100u));
            Assert.False(objective.IsTarget(999u));
        }

        [Fact]
        public void ObjectiveUpdate_OrdinaryProgressSaturatesWithoutOverflow()
        {
            QuestObjective objective = CreateObjective(QuestObjectiveType.CollectItem, uint.MaxValue);
            objective.Progress = uint.MaxValue - 1u;

            objective.ObjectiveUpdate(10u);

            Assert.Equal(uint.MaxValue, objective.Progress);
            Assert.True(objective.IsComplete());
        }

        [Fact]
        public void ObjectiveUpdate_DynamicProjectionCompletesLargestBuild16042CountExactly()
        {
            QuestObjective objective = CreateObjective(QuestObjectiveType.KillCreature, 80u);

            for (int i = 0; i < 80; i++)
                objective.ObjectiveUpdate(1u);

            Assert.Equal(1000u, objective.Progress);
            Assert.True(objective.IsComplete());
        }

        [Fact]
        public void ObjectiveUpdate_ZeroCountKillObjectiveFailsClosed()
        {
            QuestObjective objective = CreateObjective(QuestObjectiveType.KillCreature, 0u);

            objective.ObjectiveUpdate(1u);

            Assert.Equal(0u, objective.Progress);
            Assert.True(objective.IsComplete());
        }

        private static QuestObjective CreateObjective(QuestObjectiveType type, uint count)
        {
            IQuestObjectiveInfo objectiveInfo = CreateObjectiveInfo(1u, type, count);
            IQuestInfo questInfo = CreateQuestInfo(objectiveInfo);
            var assetManager = new Mock<IAssetManager>();
            assetManager.Setup(manager => manager.GetQuestObjectiveTargetIds(objectiveInfo.Id))
                .Returns(ImmutableList<uint>.Empty);
            return new QuestObjective(
                Mock.Of<IPlayer>(),
                questInfo,
                objectiveInfo,
                0,
                assetManager.Object);
        }

        internal static IQuestObjectiveInfo CreateObjectiveInfo(
            uint id,
            QuestObjectiveType type,
            uint count,
            QuestObjectiveFlags flags = QuestObjectiveFlags.None,
            uint data = 1u,
            uint targetGroupId = 0u)
        {
            return new QuestObjectiveInfo(new QuestObjectiveEntry
            {
                Id                      = id,
                Type                    = (uint)type,
                Count                   = count,
                Flags                   = (uint)flags,
                Data                    = data,
                TargetGroupIdRewardPane = targetGroupId
            });
        }

        internal static IQuestInfo CreateQuestInfo(params IQuestObjectiveInfo[] objectives)
        {
            var info = new Mock<IQuestInfo>();
            info.SetupGet(value => value.Entry).Returns(new Quest2Entry
            {
                Id               = 100u,
                PushedItemIds    = [],
                PushedItemCounts = []
            });
            info.SetupGet(value => value.Objectives).Returns(objectives.ToImmutableList());
            return info.Object;
        }
    }

    public sealed class TargetGroupResolverTests
    {
        [Fact]
        public void ResolveCreatureIds_HandlesNestedCyclesMissingGroupsAndDuplicateLeaves()
        {
            var resolver = new TargetGroupResolver(
            [
                CreateTargetGroup(1u, TargetGroupType.CreatureIdGroup, 100u, 101u, 100u),
                CreateTargetGroup(2u, TargetGroupType.OtherTargetGroup, 1u, 3u, 99u),
                CreateTargetGroup(3u, TargetGroupType.OtherTargetGroupCreatures, 4u, 1u),
                CreateTargetGroup(4u, TargetGroupType.OtherTargetGroup, 3u)
            ]);

            ImmutableList<uint> targets = resolver.ResolveCreatureIds(2u);

            Assert.Equal([100u, 101u], targets);
            Assert.Empty(resolver.ResolveCreatureIds(99u));
        }

        private static TargetGroupEntry CreateTargetGroup(
            uint id,
            TargetGroupType type,
            params uint[] entries)
        {
            return new TargetGroupEntry
            {
                Id          = id,
                Type        = (uint)type,
                DataEntries = entries
            };
        }
    }

    [Collection(QuestPersistenceCollection.Name)]
    public sealed class QuestObjectiveBehaviourTests
    {
        [Fact]
        public void ObjectiveUpdate_MatchingObjectivesEmitPacketsInAscendingIndexOrder()
        {
            QuestFixture fixture = CreateQuest(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(1u, QuestObjectiveType.CollectItem, 2u),
                QuestObjectiveChecklistTests.CreateObjectiveInfo(2u, QuestObjectiveType.CollectItem, 2u));

            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.CollectItem, 1u, 1u);

            Assert.Equal([0u, 1u], GetMessages<ServerQuestObjectiveUpdate>(fixture.Session)
                .Select(message => message.QuestObjectiveIndex));
            Assert.Equal(QuestState.Accepted, fixture.Quest.State);
        }

        [Fact]
        public void ObjectiveUpdate_ScriptFailuresCannotSuppressCommittedProgressNotificationsOrAchievement()
        {
            QuestDependencies dependencies = CreateDependencies();
            var scriptCollection = new Mock<IScriptCollection>();
            scriptCollection
                .Setup(collection => collection.Invoke<IQuestScript>(It.IsAny<Action<IQuestScript>>()))
                .Throws(new InvalidOperationException("Test quest script failure."));
            dependencies.ScriptManager
                .Setup(manager => manager.InitialiseOwnedScripts<IQuest>(
                    It.IsAny<IQuest>(), It.IsAny<uint>()))
                .Returns(scriptCollection.Object);
            var quest = new QuestEntity(
                dependencies.Player.Object,
                QuestObjectiveChecklistTests.CreateQuestInfo(
                    QuestObjectiveChecklistTests.CreateObjectiveInfo(
                        1u, QuestObjectiveType.CollectItem, 1u),
                    QuestObjectiveChecklistTests.CreateObjectiveInfo(
                        2u, QuestObjectiveType.CollectItem, 1u)),
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);

            Exception exception = Record.Exception(() => quest.ObjectiveUpdate(
                QuestObjectiveType.CollectItem, 1u, 1u));

            Assert.Null(exception);
            Assert.Equal([1u, 1u], quest.Select(objective => objective.Progress));
            Assert.Equal(QuestState.Achieved, quest.State);
            Assert.Equal([0u, 1u], GetMessages<ServerQuestObjectiveUpdate>(dependencies.Session)
                .Select(message => message.QuestObjectiveIndex));
            Assert.Single(GetMessages<ServerQuestStateChange>(dependencies.Session));
            scriptCollection.Verify(collection => collection.Invoke<IQuestScript>(
                It.IsAny<Action<IQuestScript>>()), Times.Exactly(3));
        }

        [Fact]
        public void ObjectiveUpdate_NotificationFailuresContinueInAscendingOrderAndStillAchieve()
        {
            QuestDependencies dependencies = CreateDependencies();
            var attemptedObjectiveIndices = new List<uint>();
            dependencies.Session
                .Setup(session => session.EnqueueMessageEncrypted(
                    It.IsAny<ServerQuestObjectiveUpdate>()))
                .Callback<IWritable>(message => attemptedObjectiveIndices.Add(
                    ((ServerQuestObjectiveUpdate)message).QuestObjectiveIndex))
                .Throws(new InvalidOperationException("Test objective packet failure."));
            var quest = new QuestEntity(
                dependencies.Player.Object,
                QuestObjectiveChecklistTests.CreateQuestInfo(
                    QuestObjectiveChecklistTests.CreateObjectiveInfo(
                        1u, QuestObjectiveType.CollectItem, 1u),
                    QuestObjectiveChecklistTests.CreateObjectiveInfo(
                        2u, QuestObjectiveType.CollectItem, 1u)),
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);

            Exception exception = Record.Exception(() => quest.ObjectiveUpdate(
                QuestObjectiveType.CollectItem, 1u, 1u));

            Assert.Null(exception);
            Assert.Equal([1u, 1u], quest.Select(objective => objective.Progress));
            Assert.Equal([0u, 1u], attemptedObjectiveIndices);
            Assert.Equal(QuestState.Achieved, quest.State);
            Assert.Single(GetMessages<ServerQuestStateChange>(dependencies.Session));
        }

        [Fact]
        public void ObjectiveUpdate_NotificationFailuresPreserveAchievedPersistenceAndRejectReplay()
        {
            QuestDependencies dependencies = CreateDependencies();
            dependencies.Session
                .Setup(session => session.EnqueueMessageEncrypted(
                    It.IsAny<ServerQuestObjectiveUpdate>()))
                .Throws(new InvalidOperationException("Test objective packet failure."));
            dependencies.Session
                .Setup(session => session.EnqueueMessageEncrypted(
                    It.IsAny<ServerQuestStateChange>()))
                .Throws(new InvalidOperationException("Test state packet failure."));
            IQuestInfo questInfo = QuestObjectiveChecklistTests.CreateQuestInfo(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    1u, QuestObjectiveType.CollectItem, 1u));
            CharacterQuestModel model = CreateQuestModel();
            model.QuestObjective.Add(CreateObjectiveModel(0, 0u));
            var quest = new QuestEntity(
                dependencies.Player.Object,
                questInfo,
                model,
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);

            Exception exception = Record.Exception(() => quest.ObjectiveUpdate(
                QuestObjectiveType.CollectItem, 1u, 1u));
            quest.ObjectiveUpdate(QuestObjectiveType.CollectItem, 1u, 1u);

            Assert.Null(exception);
            Assert.Equal(1u, Assert.Single(quest).Progress);
            Assert.Equal(QuestState.Achieved, quest.State);
            dependencies.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<ServerQuestObjectiveUpdate>()), Times.Once);
            dependencies.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<ServerQuestStateChange>()), Times.Once);

            using var context = new TestCharacterContext();
            quest.Save(context, new SaveCommitScope());

            var questEntry = Assert.Single(context.ChangeTracker.Entries<CharacterQuestModel>());
            Assert.Equal((byte)QuestState.Achieved, questEntry.Entity.State);
            Assert.True(questEntry.Property(value => value.State).IsModified);
            var objectiveEntry = Assert.Single(
                context.ChangeTracker.Entries<CharacterQuestObjectiveModel>());
            Assert.Equal(1u, objectiveEntry.Entity.Progress);
            Assert.True(objectiveEntry.Property(value => value.Progress).IsModified);
        }

        [Fact]
        public void ObjectiveUpdate_SequentialObjectiveWaitsForFollowingEvent()
        {
            QuestFixture fixture = CreateQuest(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(1u, QuestObjectiveType.CollectItem, 1u),
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    2u,
                    QuestObjectiveType.CollectItem,
                    1u,
                    QuestObjectiveFlags.Sequential));

            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.CollectItem, 1u, 1u);

            Assert.Equal([1u, 0u], fixture.Quest.Select(objective => objective.Progress));
            Assert.Equal([0u], GetMessages<ServerQuestObjectiveUpdate>(fixture.Session)
                .Select(message => message.QuestObjectiveIndex));

            fixture.Session.Invocations.Clear();
            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.CollectItem, 1u, 1u);

            Assert.Equal([1u, 1u], fixture.Quest.Select(objective => objective.Progress));
            Assert.Equal([1u], GetMessages<ServerQuestObjectiveUpdate>(fixture.Session)
                .Select(message => message.QuestObjectiveIndex));
            Assert.Equal(QuestState.Achieved, fixture.Quest.State);
        }

        [Fact]
        public void ObjectiveUpdate_ExcludesOnlyExactStaticObjectiveWithinDescendingBatch()
        {
            QuestFixture fixture = CreateQuest(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    1u,
                    QuestObjectiveType.ActivateEntity,
                    2u,
                    data: 77u),
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    2u,
                    QuestObjectiveType.ActivateEntity,
                    2u,
                    data: 77u));

            fixture.Quest.ObjectiveUpdate(
                QuestObjectiveType.ActivateEntity,
                77u,
                1u,
                new HashSet<uint> { 1u });

            Assert.Equal([0u, 1u], fixture.Quest.Select(objective => objective.Progress));
            Assert.Equal([1u], GetMessages<ServerQuestObjectiveUpdate>(fixture.Session)
                .Select(message => message.QuestObjectiveIndex));
            Assert.Equal(QuestState.Accepted, fixture.Quest.State);
        }

        [Fact]
        public void ObjectiveUpdate_OptionalPredecessorDoesNotBlockSequentialRequiredObjective()
        {
            QuestFixture fixture = CreateQuest(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    1u,
                    QuestObjectiveType.CollectItem,
                    5u,
                    QuestObjectiveFlags.Optional),
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    2u,
                    QuestObjectiveType.CollectItem,
                    1u,
                    QuestObjectiveFlags.Sequential));

            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.CollectItem, 1u, 1u);

            Assert.Equal([1u, 1u], fixture.Quest.Select(objective => objective.Progress));
            Assert.Equal([0u, 1u], GetMessages<ServerQuestObjectiveUpdate>(fixture.Session)
                .Select(message => message.QuestObjectiveIndex));
            Assert.Equal(QuestState.Achieved, fixture.Quest.State);
        }

        [Fact]
        public void ObjectiveUpdate_AllOptionalQuestRequiresItsOwnObjectives()
        {
            IQuestObjectiveInfo objectiveInfo = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                1u,
                QuestObjectiveType.CollectItem,
                2u,
                QuestObjectiveFlags.Optional);
            QuestFixture fixture = CreateQuest(objectiveInfo);

            fixture.Quest.ObjectiveUpdate(objectiveInfo.Id, 1u);
            Assert.Equal(QuestState.Accepted, fixture.Quest.State);

            fixture.Quest.ObjectiveUpdate(objectiveInfo.Id, 1u);
            Assert.Equal(QuestState.Achieved, fixture.Quest.State);
        }

        [Fact]
        public void ObjectiveUpdate_UncompletableOptionalChecklistDoesNotBlockRequiredObjectives()
        {
            QuestFixture fixture = CreateQuest(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    1u,
                    QuestObjectiveType.CollectItem,
                    1u),
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    2u,
                    QuestObjectiveType.ActivateTargetGroupChecklist,
                    40u,
                    QuestObjectiveFlags.Optional));

            fixture.Quest.ObjectiveUpdate(QuestObjectiveType.CollectItem, 1u, 1u);

            Assert.Equal(QuestState.Achieved, fixture.Quest.State);
            Assert.False(fixture.Quest.Last().IsComplete());
        }

        [Fact]
        public void ObjectiveComplete_CompletesOnlyTheExactChecklistObjective()
        {
            IQuestObjectiveInfo first = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                1u,
                QuestObjectiveType.ActivateTargetGroupChecklist,
                3u,
                data: 500u);
            IQuestObjectiveInfo second = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                2u,
                QuestObjectiveType.ActivateTargetGroupChecklist,
                3u,
                data: 500u);
            QuestFixture fixture = CreateQuest(first, second);

            fixture.Quest.ObjectiveComplete(first.Id);

            Assert.Equal([0x07u, 0u], fixture.Quest.Select(objective => objective.Progress));
            Assert.Equal([0u], GetMessages<ServerQuestObjectiveUpdate>(fixture.Session)
                .Select(message => message.QuestObjectiveIndex));
            Assert.Equal(QuestState.Accepted, fixture.Quest.State);
        }

        [Fact]
        public void ObjectivesComplete_UsesExactRepresentationsAndAscendingPacketOrder()
        {
            QuestFixture fixture = CreateQuest(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    1u,
                    QuestObjectiveType.ActivateTargetGroupChecklist,
                    3u),
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    2u,
                    QuestObjectiveType.CollectItem,
                    5u));

            fixture.Quest.ObjectivesComplete();

            Assert.Equal([0x07u, 5u], fixture.Quest.Select(objective => objective.Progress));
            Assert.Equal([0u, 1u], GetMessages<ServerQuestObjectiveUpdate>(fixture.Session)
                .Select(message => message.QuestObjectiveIndex));
            Assert.Equal(QuestState.Achieved, fixture.Quest.State);
        }

        [Fact]
        public void ObjectivesComplete_CannotBypassOversizedChecklistStorageLimit()
        {
            QuestFixture fixture = CreateQuest(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(
                    1u,
                    QuestObjectiveType.ActivateTargetGroupChecklist,
                    40u));

            fixture.Quest.ObjectivesComplete();

            Assert.Equal(0u, Assert.Single(fixture.Quest).Progress);
            Assert.Empty(GetMessages<ServerQuestObjectiveUpdate>(fixture.Session));
            Assert.Equal(QuestState.Accepted, fixture.Quest.State);
        }

        [Fact]
        public void ObjectiveUpdate_DuplicateChecklistBitEmitsNoSecondPacket()
        {
            IQuestObjectiveInfo objectiveInfo = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                1u,
                QuestObjectiveType.ActivateTargetGroupChecklist,
                3u);
            QuestFixture fixture = CreateQuest(objectiveInfo);

            fixture.Quest.ObjectiveUpdate(objectiveInfo.Id, 2u);
            fixture.Quest.ObjectiveUpdate(objectiveInfo.Id, 2u);

            Assert.Single(GetMessages<ServerQuestObjectiveUpdate>(fixture.Session));
            Assert.Equal(0x04u, Assert.Single(fixture.Quest).Progress);
        }

        [Fact]
        public void SimpleEntity_OnActivateSuccessRoutesRawChecklistIndex()
        {
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            var entity = new SimpleEntity(Mock.Of<NexusForever.Game.Abstract.Entity.Movement.IMovementManager>());
            typeof(SimpleEntity).GetProperty(
                    nameof(SimpleEntity.QuestChecklistIdx),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(entity, (byte)31);

            entity.OnActivateSuccess(player.Object);

            questManager.Verify(manager => manager.ObjectiveUpdate(
                QuestObjectiveType.ActivateTargetGroupChecklist,
                entity.CreatureId,
                31u), Times.Once);
        }

        [Theory]
        [InlineData((byte)32)]
        [InlineData(byte.MaxValue)]
        public void SimpleEntity_OnActivateSuccessRejectsChecklistIndexOutsideDatacubeMask(byte checklistIndex)
        {
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            var entity = new SimpleEntity(Mock.Of<NexusForever.Game.Abstract.Entity.Movement.IMovementManager>());
            typeof(SimpleEntity).GetProperty(
                    nameof(SimpleEntity.QuestChecklistIdx),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(entity, checklistIndex);

            Exception exception = Record.Exception(() => entity.OnActivateSuccess(player.Object));

            Assert.Null(exception);
            questManager.Verify(manager => manager.ObjectiveUpdate(
                It.IsAny<QuestObjectiveType>(),
                It.IsAny<uint>(),
                It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void SimpleEntity_DatacubeFailureDoesNotSuppressVolumeOrChecklistProgress()
        {
            var datacubeManager = new Mock<IDatacubeManager>();
            datacubeManager.Setup(manager => manager.GetDatacube(11, DatacubeType.Datacube))
                .Throws(new InvalidOperationException("Test datacube failure."));
            datacubeManager.Setup(manager => manager.GetDatacube(12, DatacubeType.Journal))
                .Returns((IDatacube)null);
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.DatacubeManager).Returns(datacubeManager.Object);
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            var entity = new SimpleEntity(Mock.Of<NexusForever.Game.Abstract.Entity.Movement.IMovementManager>());
            typeof(WorldEntity).GetProperty(
                    nameof(WorldEntity.CreatureEntry),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(entity, new Creature2Entry
                {
                    DatacubeId       = 11u,
                    DatacubeVolumeId = 12u
                });
            typeof(SimpleEntity).GetProperty(
                    nameof(SimpleEntity.QuestChecklistIdx),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(entity, (byte)3);

            entity.OnActivateSuccess(player.Object);

            datacubeManager.Verify(manager => manager.AddDatacubeVolume(12, 0x08u), Times.Once);
            questManager.Verify(manager => manager.ObjectiveUpdate(
                QuestObjectiveType.ActivateTargetGroupChecklist,
                entity.CreatureId,
                3u), Times.Once);
        }

        [Fact]
        public void Constructor_LoadedObjectivesAreValidatedAndIndexOrdered()
        {
            IQuestObjectiveInfo first = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                1u, QuestObjectiveType.CollectItem, 20u);
            IQuestObjectiveInfo second = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                2u, QuestObjectiveType.CollectItem, 20u);
            IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo(first, second);
            QuestDependencies dependencies = CreateDependencies();
            CharacterQuestModel model = CreateQuestModel();
            model.QuestObjective.Add(CreateObjectiveModel(1, 10u));
            model.QuestObjective.Add(CreateObjectiveModel(0, 5u));
            model.QuestObjective.Add(CreateObjectiveModel(9, 99u));

            var quest = new QuestEntity(
                dependencies.Player.Object,
                info,
                model,
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);

            Assert.Equal([0, 1], quest.Select(objective => objective.Index));
            Assert.Equal([5u, 10u], quest.Select(objective => objective.Progress));
        }

        [Fact]
        public void Constructor_MissingAndDuplicateLoadedIndicesReconstructStaticShape()
        {
            IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(1u, QuestObjectiveType.CollectItem, 20u),
                QuestObjectiveChecklistTests.CreateObjectiveInfo(2u, QuestObjectiveType.CollectItem, 20u));
            QuestDependencies dependencies = CreateDependencies();
            CharacterQuestModel model = CreateQuestModel();
            model.QuestObjective.Add(CreateObjectiveModel(0, 5u));
            model.QuestObjective.Add(CreateObjectiveModel(0, 7u));

            var quest = new QuestEntity(
                dependencies.Player.Object,
                info,
                model,
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);

            Assert.Equal([0, 1], quest.Select(objective => objective.Index));
            Assert.Equal(0u, quest.Single(objective => objective.Index == 1).Progress);
        }

        [Fact]
        public void SendInitialPackets_PreservesObjectiveIndexOrder()
        {
            IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo(
                QuestObjectiveChecklistTests.CreateObjectiveInfo(1u, QuestObjectiveType.CollectItem, 20u),
                QuestObjectiveChecklistTests.CreateObjectiveInfo(2u, QuestObjectiveType.CollectItem, 20u));
            QuestDependencies dependencies = CreateDependencies();
            CharacterQuestModel model = CreateQuestModel();
            model.QuestObjective.Add(CreateObjectiveModel(1, 10u));
            model.QuestObjective.Add(CreateObjectiveModel(0, 5u));
            var quest = new QuestEntity(
                dependencies.Player.Object,
                info,
                model,
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);
            var manager = new QuestManager(
                dependencies.Player.Object,
                new CharacterModel(),
                dependencies.GlobalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>());
            GetActiveQuests(manager).Add(quest.Id, quest);

            manager.SendInitialPackets();

            ServerQuestInit message = Assert.Single(GetMessages<ServerQuestInit>(dependencies.Session));
            Assert.Equal([5u, 10u], Assert.Single(message.Active).Objectives.Select(objective => objective.Progress));
        }

        [Fact]
        public void QuestManager_TryObjectiveUpdate_UsesExactSlotAndRawAmount()
        {
            IQuestObjectiveInfo first = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                1u,
                QuestObjectiveType.CollectItem,
                5u);
            IQuestObjectiveInfo second = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                2u,
                QuestObjectiveType.CollectItem,
                5u);
            QuestDependencies dependencies = CreateDependencies();
            IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo(first, second);
            var quest = new QuestEntity(
                dependencies.Player.Object,
                info,
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);
            var manager = new QuestManager(
                dependencies.Player.Object,
                new CharacterModel(),
                dependencies.GlobalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>());
            GetActiveQuests(manager).Add(quest.Id, quest);

            bool updated = manager.TryObjectiveUpdate(quest.Id, 1, 2u, out IQuestObjective objective);

            Assert.True(updated);
            Assert.Same(quest.ElementAt(1), objective);
            Assert.Equal([0u, 2u], quest.Select(value => value.Progress));
            ServerQuestObjectiveUpdate message = Assert.Single(
                GetMessages<ServerQuestObjectiveUpdate>(dependencies.Session));
            Assert.Equal(1u, message.QuestObjectiveIndex);
            Assert.Equal(2u, message.Completed);
        }

        [Fact]
        public void QuestManager_TryObjectiveUpdate_PreservesChecklistRawValueAndRejectsInvalidInput()
        {
            IQuestObjectiveInfo objectiveInfo = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                1u,
                QuestObjectiveType.ActivateTargetGroupChecklist,
                4u);
            QuestDependencies dependencies = CreateDependencies();
            IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo(objectiveInfo);
            var quest = new QuestEntity(
                dependencies.Player.Object,
                info,
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);
            var manager = new QuestManager(
                dependencies.Player.Object,
                new CharacterModel(),
                dependencies.GlobalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>());
            GetActiveQuests(manager).Add(quest.Id, quest);

            Assert.True(manager.TryObjectiveUpdate(quest.Id, 0, 2u, out IQuestObjective objective));
            Assert.Equal(0x04u, objective.Progress);
            Assert.False(manager.TryObjectiveUpdate(quest.Id, 1, 1u, out _));
            Assert.False(manager.TryObjectiveUpdate(quest.Id, 0, 0u, out _));
            Assert.False(manager.TryObjectiveUpdate(999, 0, 1u, out _));
        }

        [Fact]
        public void QuestManager_TryObjectiveUpdate_NotificationFailureCannotReplayCommittedProgress()
        {
            IQuestObjectiveInfo objectiveInfo = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                1u,
                QuestObjectiveType.ActivateEntity,
                1u,
                data: 24_251u);
            QuestDependencies dependencies = CreateDependencies();
            dependencies.Session
                .Setup(session => session.EnqueueMessageEncrypted(
                    It.IsAny<ServerQuestObjectiveUpdate>()))
                .Throws(new InvalidOperationException("Test objective packet failure."));
            IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo(objectiveInfo);
            var quest = new QuestEntity(
                dependencies.Player.Object,
                info,
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);
            var manager = new QuestManager(
                dependencies.Player.Object,
                new CharacterModel(),
                dependencies.GlobalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>());
            GetActiveQuests(manager).Add(quest.Id, quest);

            bool updated = manager.TryObjectiveUpdate(quest.Id, 0, 1u, out IQuestObjective objective);
            bool replayed = manager.TryObjectiveUpdate(quest.Id, 0, 1u, out _);

            Assert.True(updated);
            Assert.False(replayed);
            Assert.Equal(1u, objective.Progress);
            Assert.Equal(QuestState.Achieved, quest.State);
            dependencies.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<ServerQuestObjectiveUpdate>()), Times.Once);
            dependencies.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<ServerQuestStateChange>()), Times.Once);
        }

        [Fact]
        public void Constructor_EmptyQuestIsImmediatelyAchieved()
        {
            QuestFixture fixture = CreateQuest();

            Assert.Equal(QuestState.Achieved, fixture.Quest.State);
            Assert.Empty(fixture.Quest);
        }

        [Fact]
        public void QuestManager_EmptyQuestRemainsAchievedWhenAdded()
        {
            IServiceProvider previousProvider = LegacyServiceProvider.Provider;
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton(Mock.Of<IScriptManager>())
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = provider;

            try
            {
                QuestDependencies dependencies = CreateDependencies();
                dependencies.Player.SetupGet(player => player.Inventory).Returns(Mock.Of<IInventory>(
                    inventory => inventory.GetInventorySlotsRemaining(InventoryLocation.Inventory) == 10u));
                IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo();
                var manager = new QuestManager(
                    dependencies.Player.Object,
                    new CharacterModel(),
                    dependencies.GlobalQuestManager.Object,
                    null,
                    Mock.Of<IDisableManager>());

                manager.QuestAdd(info);

                Assert.Equal(QuestState.Achieved, Assert.Single(manager.GetActiveQuests()).State);
                Assert.Equal(QuestState.Achieved,
                    Assert.Single(GetMessages<ServerQuestStateChange>(dependencies.Session)).QuestState);
            }
            finally
            {
                LegacyServiceProvider.Provider = previousProvider;
            }
        }

        [Fact]
        public void HasActiveQuestCapacity_RejectsAtConfiguredMaximum()
        {
            QuestDependencies dependencies = CreateDependencies();
            var manager = new QuestManager(
                dependencies.Player.Object,
                new CharacterModel(),
                dependencies.GlobalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>());
            Dictionary<ushort, IQuest> activeQuests = GetActiveQuests(manager);
            for (ushort questId = 1; questId <= 40; questId++)
                activeQuests.Add(questId, Mock.Of<IQuest>(quest => quest.Id == questId));

            Assert.False(manager.HasActiveQuestCapacity(40u));

            activeQuests.Remove(40);
            Assert.True(manager.HasActiveQuestCapacity(40u));
        }

        private static QuestFixture CreateQuest(params IQuestObjectiveInfo[] objectives)
        {
            QuestDependencies dependencies = CreateDependencies();
            IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo(objectives);
            var quest = new QuestEntity(
                dependencies.Player.Object,
                info,
                dependencies.GlobalQuestManager.Object,
                dependencies.ScriptManager.Object,
                dependencies.AssetManager.Object);
            return new QuestFixture(quest, dependencies.Session);
        }

        private static QuestDependencies CreateDependencies()
        {
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

            return new QuestDependencies(
                player,
                session,
                globalQuestManager,
                new Mock<IScriptManager>(),
                assetManager);
        }

        private static CharacterQuestModel CreateQuestModel()
        {
            return new CharacterQuestModel
            {
                Id      = 42ul,
                QuestId = 100,
                State   = (byte)QuestState.Accepted
            };
        }

        private static CharacterQuestObjectiveModel CreateObjectiveModel(byte index, uint progress)
        {
            return new CharacterQuestObjectiveModel
            {
                Id       = 42ul,
                QuestId  = 100,
                Index    = index,
                Progress = progress
            };
        }

        private static Dictionary<ushort, IQuest> GetActiveQuests(QuestManager manager)
        {
            FieldInfo field = typeof(QuestManager).GetField(
                "activeQuests",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (Dictionary<ushort, IQuest>)field.GetValue(manager);
        }

        private static IReadOnlyList<T> GetMessages<T>(Mock<IGameSession> session) where T : IWritable
        {
            return session.Invocations
                .Where(invocation => invocation.Method.Name == nameof(IGameSession.EnqueueMessageEncrypted))
                .Select(invocation => invocation.Arguments[0])
                .OfType<T>()
                .ToArray();
        }

        private sealed record QuestFixture(QuestEntity Quest, Mock<IGameSession> Session);

        private sealed record QuestDependencies(
            Mock<IPlayer> Player,
            Mock<IGameSession> Session,
            Mock<IGlobalQuestManager> GlobalQuestManager,
            Mock<IScriptManager> ScriptManager,
            Mock<IAssetManager> AssetManager);

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
