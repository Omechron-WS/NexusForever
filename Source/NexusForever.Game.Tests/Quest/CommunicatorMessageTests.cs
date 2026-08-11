using System.Collections.Immutable;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Achievement;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Abstract.Reputation;
using NexusForever.Game.Entity;
using NexusForever.Game.Quest;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reputation;
using NexusForever.Game.Tests.Persistence;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Story;
using NexusForever.Script;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using QuestEntity = NexusForever.Game.Quest.Quest;

namespace NexusForever.Game.Tests.Quest
{
    [Collection(QuestPersistenceCollection.Name)]
    public sealed class CommunicatorMessageTests : IDisposable
    {
        private const ushort TriggerQuestId = 100;
        private const ushort DeliveredQuestId = 200;

        private readonly IServiceProvider originalServiceProvider;
        private readonly ServiceProvider serviceProvider;

        public CommunicatorMessageTests()
        {
            originalServiceProvider = LegacyServiceProvider.Provider;
            serviceProvider = new ServiceCollection()
                .AddSingleton(Mock.Of<IScriptManager>())
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        [Fact]
        public void Meets_UsesInclusiveLevelBoundsAndRejectsInvertedBounds()
        {
            CommunicatorMessagesEntry entry = CreateEntry(1u);
            entry.MinLevel = 10u;
            entry.MaxLevel = 20u;
            var message = new CommunicatorMessage(entry);

            Assert.False(message.Meets(CreatePlayer(level: 9u).Object));
            Assert.True(message.Meets(CreatePlayer(level: 10u).Object));
            Assert.True(message.Meets(CreatePlayer(level: 20u).Object));
            Assert.False(message.Meets(CreatePlayer(level: 21u).Object));

            entry.MinLevel = 70u;
            entry.MaxLevel = 10u;
            Assert.False(message.Meets(CreatePlayer(level: 50u).Object));
        }

        [Fact]
        public void Meets_EnforcesRaceClassFactionAndWorldConditions()
        {
            CommunicatorMessagesEntry entry = CreateEntry(1u);
            entry.RaceId = (uint)Race.Granok;
            entry.ClassId = (uint)Class.Engineer;
            entry.FactionId = (uint)Faction.Exile;
            entry.WorldId = 22u;
            var message = new CommunicatorMessage(entry);
            Mock<IPlayer> player = CreatePlayer(
                race: Race.Granok,
                @class: Class.Engineer,
                faction: Faction.Exile);
            var map = new Mock<IBaseMap>();
            map.SetupGet(value => value.Entry).Returns(new WorldEntry { Id = 22u });
            player.SetupGet(value => value.Map).Returns(map.Object);

            Assert.True(message.Meets(player.Object));

            player.SetupGet(value => value.Race).Returns(Race.Human);
            Assert.False(message.Meets(player.Object));
            player.SetupGet(value => value.Race).Returns(Race.Granok);
            player.SetupGet(value => value.Map).Returns((IBaseMap)null);
            Assert.False(message.Meets(player.Object));

            entry.ClassId = byte.MaxValue + 1u;
            Assert.False(message.Meets(player.Object));
        }

        [Fact]
        public void Meets_UsesExactQuestStateAndFailsClosedForMalformedArrays()
        {
            CommunicatorMessagesEntry entry = CreateEntry(1u);
            entry.Quests = [TriggerQuestId];
            entry.States = [(uint)QuestState.Achieved];
            var message = new CommunicatorMessage(entry);
            Mock<IPlayer> player = CreatePlayer();
            var questManager = new Mock<IQuestManager>();
            questManager.Setup(manager => manager.GetQuestState(TriggerQuestId)).Returns(QuestState.Achieved);
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);

            Assert.True(message.Meets(player.Object));

            questManager.Setup(manager => manager.GetQuestState(TriggerQuestId)).Returns(QuestState.Completed);
            Assert.False(message.Meets(player.Object));
            player.SetupGet(value => value.QuestManager).Returns((IQuestManager)null);
            Assert.False(message.Meets(player.Object));

            entry.Quests = null;
            Assert.False(message.Meets(player.Object));
            entry.Quests = [TriggerQuestId];
            entry.States = [];
            Assert.False(message.Meets(player.Object));
            Assert.False(message.Meets(null));
        }

        [Fact]
        public void Meets_RejectsQuestReferencesOutsideBuild16042Field()
        {
            CommunicatorMessagesEntry delivered = CreateEntry(1u);
            delivered.QuestIdDelivered = CommunicatorMessage.MaximumId + 1u;
            var deliveredMessage = new CommunicatorMessage(delivered);

            Assert.False(deliveredMessage.Meets(CreatePlayer().Object));
            Assert.Throws<InvalidDataException>(() => deliveredMessage.QuestId);

            CommunicatorMessagesEntry trigger = CreateEntry(2u);
            trigger.Quests = [CommunicatorMessage.MaximumId + 1u];
            trigger.States = [(uint)QuestState.Achieved];
            Assert.False(new CommunicatorMessage(trigger).Meets(CreatePlayer().Object));
        }

        [Fact]
        public void Meets_AppliesInclusiveReputationBoundsAndTreatsMissingRecordAsZero()
        {
            CommunicatorMessagesEntry entry = CreateEntry(1u);
            entry.FactionIdReputation = (uint)Faction.Exile;
            entry.ReputationMin = 100u;
            entry.ReputationMax = 200u;
            var message = new CommunicatorMessage(entry);

            Assert.False(message.Meets(CreatePlayer(reputationAmount: 99f).Object));
            Assert.True(message.Meets(CreatePlayer(reputationAmount: 100f).Object));
            Assert.True(message.Meets(CreatePlayer(reputationAmount: 200f).Object));
            Assert.False(message.Meets(CreatePlayer(reputationAmount: 201f).Object));

            entry.ReputationMin = 0u;
            entry.ReputationMax = 10u;
            Assert.True(message.Meets(CreatePlayer(hasReputation: false).Object));
            entry.ReputationMin = 1u;
            Assert.False(message.Meets(CreatePlayer(hasReputation: false).Object));
        }

        [Fact]
        public void Meets_ReputationAndPrerequisiteFailuresFailClosed()
        {
            CommunicatorMessagesEntry entry = CreateEntry(1u);
            entry.FactionIdReputation = (uint)Faction.Exile;
            entry.ReputationMin = 200u;
            entry.ReputationMax = 100u;
            var message = new CommunicatorMessage(entry);
            Assert.False(message.Meets(CreatePlayer(reputationAmount: 150f).Object));

            entry.ReputationMin = 1u;
            entry.ReputationMax = 0u;
            Assert.False(message.Meets(CreatePlayer(reputationAmount: float.NaN).Object));
            Mock<IPlayer> player = CreatePlayer();
            player.SetupGet(value => value.ReputationManager).Returns((IReputationManager)null);
            Assert.False(message.Meets(player.Object));

            entry.ReputationMin = 0u;
            entry.PrerequisiteId = 44u;
            var prerequisiteManager = new Mock<IPrerequisiteManager>();
            prerequisiteManager
                .Setup(manager => manager.Meets(player.Object, entry.PrerequisiteId))
                .Throws<InvalidOperationException>();
            message = new CommunicatorMessage(entry, prerequisiteManager.Object);
            Assert.False(message.Meets(player.Object));
        }

        [Fact]
        public void Send_WritesExactBuild16042MessageIdAndRejectsOutOfRangeId()
        {
            var session = new Mock<IGameSession>();
            var message = new CommunicatorMessage(CreateEntry(8_199u));

            message.Send(session.Object);

            ServerCommunicatorMessage packet = Assert.Single(GetMessages<ServerCommunicatorMessage>(session));
            Assert.Equal(8_199, packet.CommunicatorMessagesId);

            var invalid = new CommunicatorMessage(CreateEntry(CommunicatorMessage.MaximumId + 1u));
            Assert.Throws<InvalidDataException>(() => invalid.Send(session.Object));
            Assert.Single(GetMessages<ServerCommunicatorMessage>(session));
        }

        [Fact]
        public void InitialiseCommunicators_CachesStoryOnlyTriggersInMessageIdOrderAndDeduplicates()
        {
            var manager = new GlobalQuestManager();
            IReadOnlyDictionary<ushort, IQuestInfo> quests = CreateQuestStore(TriggerQuestId, DeliveredQuestId);
            CommunicatorMessagesEntry delivered = CreateEntry(10u);
            delivered.QuestIdDelivered = DeliveredQuestId;
            delivered.Quests = [TriggerQuestId];
            delivered.States = [(uint)QuestState.Achieved];
            CommunicatorMessagesEntry storyOnly = CreateEntry(30u);
            storyOnly.Quests = [TriggerQuestId, TriggerQuestId];
            storyOnly.States = [(uint)QuestState.Achieved, (uint)QuestState.Achieved];
            CommunicatorMessagesEntry highState = CreateEntry(40u);
            highState.Quests = [TriggerQuestId];
            highState.States = [99u];

            manager.InitialiseCommunicators([storyOnly, highState, delivered], quests);

            Assert.Equal([10u, 30u], manager
                .GetQuestCommunicatorQuestStateTriggers(TriggerQuestId, QuestState.Achieved)
                .Select(message => message.Id));
            Assert.Equal([40u], manager
                .GetQuestCommunicatorQuestStateTriggers(TriggerQuestId, (QuestState)99)
                .Select(message => message.Id));
            Assert.Equal([10u], manager
                .GetQuestCommunicatorMessages(DeliveredQuestId)
                .Select(message => message.Id));
        }

        [Fact]
        public void InitialiseCommunicators_AcceptsMaximumBuild16042Ids()
        {
            const ushort maximumId = (ushort)CommunicatorMessage.MaximumId;
            var manager = new GlobalQuestManager();
            IReadOnlyDictionary<ushort, IQuestInfo> quests = CreateQuestStore(TriggerQuestId, maximumId);
            CommunicatorMessagesEntry entry = CreateTriggerEntry(CommunicatorMessage.MaximumId, TriggerQuestId);
            entry.QuestIdDelivered = maximumId;

            manager.InitialiseCommunicators([entry], quests);

            Assert.Equal([CommunicatorMessage.MaximumId], manager
                .GetQuestCommunicatorMessages(maximumId)
                .Select(message => message.Id));
            Assert.Equal([CommunicatorMessage.MaximumId], manager
                .GetQuestCommunicatorQuestStateTriggers(TriggerQuestId, QuestState.Achieved)
                .Select(message => message.Id));
        }

        [Fact]
        public void InitialiseCommunicators_RejectsMalformedRowsWithoutSuppressingValidRows()
        {
            var manager = new GlobalQuestManager();
            IReadOnlyDictionary<ushort, IQuestInfo> quests = CreateQuestStore(TriggerQuestId, DeliveredQuestId);
            CommunicatorMessagesEntry zeroId = CreateTriggerEntry(0u, TriggerQuestId);
            CommunicatorMessagesEntry oversizedId = CreateTriggerEntry(CommunicatorMessage.MaximumId + 1u, TriggerQuestId);
            CommunicatorMessagesEntry missingDelivered = CreateTriggerEntry(3u, TriggerQuestId);
            missingDelivered.QuestIdDelivered = 201u;
            CommunicatorMessagesEntry oversizedDelivered = CreateTriggerEntry(4u, TriggerQuestId);
            oversizedDelivered.QuestIdDelivered = CommunicatorMessage.MaximumId + 1u;
            CommunicatorMessagesEntry missingTrigger = CreateTriggerEntry(5u, 999u);
            CommunicatorMessagesEntry oversizedTrigger = CreateTriggerEntry(6u, CommunicatorMessage.MaximumId + 1u);
            CommunicatorMessagesEntry nullArray = CreateEntry(7u);
            nullArray.Quests = null;
            CommunicatorMessagesEntry mismatchedArrays = CreateEntry(8u);
            mismatchedArrays.Quests = [TriggerQuestId];
            CommunicatorMessagesEntry duplicateA = CreateTriggerEntry(9u, TriggerQuestId);
            CommunicatorMessagesEntry duplicateB = CreateTriggerEntry(9u, TriggerQuestId);
            CommunicatorMessagesEntry valid = CreateTriggerEntry(60u, TriggerQuestId);

            manager.InitialiseCommunicators(
            [
                zeroId,
                oversizedId,
                missingDelivered,
                oversizedDelivered,
                missingTrigger,
                oversizedTrigger,
                nullArray,
                mismatchedArrays,
                duplicateA,
                duplicateB,
                valid
            ], quests);

            Assert.Equal([60u], manager
                .GetQuestCommunicatorQuestStateTriggers(TriggerQuestId, QuestState.Achieved)
                .Select(message => message.Id));
            Assert.Empty(manager.GetQuestCommunicatorMessages(DeliveredQuestId));
        }

        [Fact]
        public void StateChange_SendsEachExactMessageBeforeMentioningDeliveredQuestOnce()
        {
            var session = new Mock<IGameSession>();
            var events = new List<string>();
            session
                .Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(packet =>
                {
                    if (packet is ServerCommunicatorMessage communicator)
                        events.Add($"send:{communicator.CommunicatorMessagesId}");
                });
            var questManager = new Mock<IQuestManager>();
            questManager
                .Setup(manager => manager.QuestMention(DeliveredQuestId))
                .Callback(() => events.Add("mention"));
            Mock<IPlayer> player = CreatePlayer();
            player.SetupGet(value => value.Session).Returns(session.Object);
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            CommunicatorMessagesEntry firstEntry = CreateEntry(11u);
            firstEntry.QuestIdDelivered = DeliveredQuestId;
            CommunicatorMessagesEntry secondEntry = CreateEntry(12u);
            secondEntry.QuestIdDelivered = DeliveredQuestId;
            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager
                .Setup(manager => manager.GetQuestCommunicatorQuestStateTriggers(TriggerQuestId, QuestState.Achieved))
                .Returns([new CommunicatorMessage(firstEntry), new CommunicatorMessage(secondEntry)]);
            globalQuestManager
                .Setup(manager => manager.GetQuestInfo(DeliveredQuestId))
                .Returns(CreateQuestInfo(DeliveredQuestId));
            QuestEntity quest = CreateQuest(player.Object, globalQuestManager.Object);

            quest.State = QuestState.Achieved;

            Assert.Equal(["send:11", "mention", "send:12"], events);
            questManager.Verify(manager => manager.QuestMention(DeliveredQuestId), Times.Once);
        }

        [Fact]
        public void StateChange_StoryOnlyMessageSendsWithoutQuestMention()
        {
            var session = new Mock<IGameSession>();
            var questManager = new Mock<IQuestManager>();
            Mock<IPlayer> player = CreatePlayer();
            player.SetupGet(value => value.Session).Returns(session.Object);
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager
                .Setup(manager => manager.GetQuestCommunicatorQuestStateTriggers(TriggerQuestId, QuestState.Achieved))
                .Returns([new CommunicatorMessage(CreateEntry(8_199u))]);
            QuestEntity quest = CreateQuest(player.Object, globalQuestManager.Object);

            quest.State = QuestState.Achieved;

            Assert.Equal(8_199, Assert.Single(GetMessages<ServerCommunicatorMessage>(session)).CommunicatorMessagesId);
            questManager.Verify(manager => manager.QuestMention(It.IsAny<ushort>()), Times.Never);
        }

        [Fact]
        public void StateChange_IsolatesThrowingMessagesAndAlwaysInvokesQuestScript()
        {
            var badCondition = new Mock<ICommunicatorMessage>();
            badCondition.Setup(message => message.Meets(It.IsAny<IPlayer>())).Throws<InvalidDataException>();
            var badSend = new Mock<ICommunicatorMessage>();
            badSend.Setup(message => message.Meets(It.IsAny<IPlayer>())).Returns(true);
            badSend.Setup(message => message.Send(It.IsAny<IGameSession>())).Throws<InvalidOperationException>();
            var good = new Mock<ICommunicatorMessage>();
            good.Setup(message => message.Meets(It.IsAny<IPlayer>())).Returns(true);
            good.SetupGet(message => message.QuestId).Returns(0);
            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager
                .Setup(manager => manager.GetQuestCommunicatorQuestStateTriggers(TriggerQuestId, QuestState.Achieved))
                .Returns([null, badCondition.Object, badSend.Object, good.Object]);
            var questScript = new Mock<IQuestScript>();
            var scriptCollection = new Mock<IScriptCollection>();
            scriptCollection
                .Setup(collection => collection.Invoke<IQuestScript>(It.IsAny<Action<IQuestScript>>()))
                .Callback<Action<IQuestScript>>(callback => callback(questScript.Object));
            var scriptManager = new Mock<IScriptManager>();
            scriptManager
                .Setup(manager => manager.InitialiseOwnedScripts<IQuest>(It.IsAny<IQuest>(), TriggerQuestId))
                .Returns(scriptCollection.Object);
            Mock<IPlayer> player = CreatePlayer();
            player.SetupGet(value => value.Session).Returns(Mock.Of<IGameSession>());
            QuestEntity quest = CreateQuest(player.Object, globalQuestManager.Object, scriptManager.Object);

            quest.State = QuestState.Achieved;

            good.Verify(message => message.Send(player.Object.Session), Times.Once);
            questScript.Verify(script => script.OnQuestStateChange(QuestState.Achieved, QuestState.Accepted), Times.Once);
        }

        [Fact]
        public void StateChange_CacheFailureDoesNotSuppressQuestScript()
        {
            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager
                .Setup(manager => manager.GetQuestCommunicatorQuestStateTriggers(TriggerQuestId, QuestState.Achieved))
                .Throws<InvalidOperationException>();
            var questScript = new Mock<IQuestScript>();
            var scriptCollection = new Mock<IScriptCollection>();
            scriptCollection
                .Setup(collection => collection.Invoke<IQuestScript>(It.IsAny<Action<IQuestScript>>()))
                .Callback<Action<IQuestScript>>(callback => callback(questScript.Object));
            var scriptManager = new Mock<IScriptManager>();
            scriptManager
                .Setup(manager => manager.InitialiseOwnedScripts<IQuest>(It.IsAny<IQuest>(), TriggerQuestId))
                .Returns(scriptCollection.Object);
            Mock<IPlayer> player = CreatePlayer();
            player.SetupGet(value => value.Session).Returns(Mock.Of<IGameSession>());
            QuestEntity quest = CreateQuest(player.Object, globalQuestManager.Object, scriptManager.Object);

            quest.State = QuestState.Achieved;

            questScript.Verify(script => script.OnQuestStateChange(QuestState.Achieved, QuestState.Accepted), Times.Once);
        }

        [Fact]
        public void QuestMention_DuplicateAfterAcknowledgementStagesNoSecondWrite()
        {
            var session = new Mock<IGameSession>();
            Mock<IPlayer> player = CreatePlayer();
            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.Session).Returns(session.Object);
            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager
                .Setup(manager => manager.GetQuestCommunicatorQuestStateTriggers(
                    It.IsAny<ushort>(),
                    It.IsAny<QuestState>()))
                .Returns([]);
            var manager = new QuestManager(
                player.Object,
                new CharacterModel { Id = 42ul },
                globalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>());
            player.SetupGet(value => value.QuestManager).Returns(manager);
            IQuestInfo info = CreateQuestInfo(DeliveredQuestId);

            manager.QuestMention(info);
            var scope = new SaveCommitScope();
            using (TestCharacterContext context = CreateContext())
            {
                manager.Save(context, scope);
                Assert.Single(context.ChangeTracker.Entries<CharacterQuestModel>());
            }
            scope.CreateAcknowledgement().Acknowledge();

            manager.QuestMention(info);
            using TestCharacterContext retryContext = CreateContext();
            manager.Save(retryContext, new SaveCommitScope());

            Assert.Empty(retryContext.ChangeTracker.Entries<CharacterQuestModel>());
            Assert.Single(GetMessages<ServerQuestStateChange>(session));
        }

        [Fact]
        public void QuestMention_RejectsQuestIdOutsideBuild16042Field()
        {
            var manager = new QuestManager(
                CreatePlayer().Object,
                new CharacterModel(),
                Mock.Of<IGlobalQuestManager>(),
                null,
                Mock.Of<IDisableManager>());
            var info = new Mock<IQuestInfo>();
            info.SetupGet(value => value.Entry).Returns(new Quest2Entry
            {
                Id = CommunicatorMessage.MaximumId + 1u
            });

            Assert.Throws<ArgumentException>(() => manager.QuestMention(info.Object));
        }

        [Fact]
        public void QuestAdd_CallbackFlagAllowsRemotePickupWithoutCommunicatorRow()
        {
            QuestManager manager = CreateQuestManagerForPickup(
                canBeCalledBack: true,
                isCommunicatorReceived: false,
                out _);

            manager.QuestAdd(DeliveredQuestId, null);

            Assert.Equal(DeliveredQuestId, Assert.Single(manager.GetActiveQuests()).Id);
        }

        [Fact]
        public void QuestAdd_UnauthorisedCommunicatorRowCannotEnableRemotePickup()
        {
            QuestManager manager = CreateQuestManagerForPickup(
                canBeCalledBack: false,
                isCommunicatorReceived: false,
                out Mock<IGlobalQuestManager> globalQuestManager);
            var message = new Mock<ICommunicatorMessage>();
            message.Setup(value => value.Meets(It.IsAny<IPlayer>())).Returns(true);
            globalQuestManager
                .Setup(value => value.GetQuestCommunicatorMessages(DeliveredQuestId))
                .Returns([message.Object]);

            Assert.Throws<QuestException>(() => manager.QuestAdd(DeliveredQuestId, null));
            Assert.Empty(manager.GetActiveQuests());
        }

        [Fact]
        public void QuestAdd_ReceivedFlagSkipsThrowingRowAndUsesLaterEligibleMessage()
        {
            QuestManager manager = CreateQuestManagerForPickup(
                canBeCalledBack: false,
                isCommunicatorReceived: true,
                out Mock<IGlobalQuestManager> globalQuestManager);
            var malformed = new Mock<ICommunicatorMessage>();
            malformed.Setup(value => value.Meets(It.IsAny<IPlayer>())).Throws<InvalidDataException>();
            var eligible = new Mock<ICommunicatorMessage>();
            eligible.Setup(value => value.Meets(It.IsAny<IPlayer>())).Returns(true);
            globalQuestManager
                .Setup(value => value.GetQuestCommunicatorMessages(DeliveredQuestId))
                .Returns([malformed.Object, eligible.Object]);

            manager.QuestAdd(DeliveredQuestId, null);

            Assert.Equal(DeliveredQuestId, Assert.Single(manager.GetActiveQuests()).Id);
        }

        [Fact]
        public void QuestComplete_CallbackFlagAllowsCommunicatorHandIn()
        {
            CompletionFixture fixture = CreateCompletionFixture(canBeCalledBack: true, isCommunicatorReceived: false);

            fixture.Manager.QuestComplete(DeliveredQuestId, 0, true);

            Assert.Equal(QuestState.Completed, fixture.Quest.Object.State);
            Assert.Equal(1, fixture.RewardManager.ApplyCount);
        }

        [Fact]
        public void QuestComplete_RejectsCommunicatorHandInWithoutEitherFlag()
        {
            CompletionFixture fixture = CreateCompletionFixture(canBeCalledBack: false, isCommunicatorReceived: false);

            Assert.Throws<QuestException>(() => fixture.Manager.QuestComplete(DeliveredQuestId, 0, true));

            Assert.Equal(QuestState.Achieved, fixture.Quest.Object.State);
            Assert.Equal(0, fixture.RewardManager.ApplyCount);
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = originalServiceProvider;
            serviceProvider.Dispose();
        }

        private static QuestManager CreateQuestManagerForPickup(
            bool canBeCalledBack,
            bool isCommunicatorReceived,
            out Mock<IGlobalQuestManager> globalQuestManager)
        {
            var session = new Mock<IGameSession>();
            var inventory = new Mock<IInventory>();
            inventory
                .Setup(value => value.GetInventorySlotsRemaining(InventoryLocation.Inventory))
                .Returns(10u);
            Mock<IPlayer> player = CreatePlayer(faction: Faction.Exile);
            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.Session).Returns(session.Object);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);
            player
                .Setup(value => value.GetVisibleCreature<WorldEntity>(It.IsAny<uint>()))
                .Returns([]);
            IQuestInfo info = CreateQuestInfo(
                DeliveredQuestId,
                canBeCalledBack,
                isCommunicatorReceived,
                isContract: true);
            globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager.Setup(value => value.GetQuestInfo(DeliveredQuestId)).Returns(info);
            globalQuestManager.Setup(value => value.GetQuestGivers(DeliveredQuestId)).Returns([]);
            globalQuestManager
                .Setup(value => value.GetQuestCommunicatorMessages(DeliveredQuestId))
                .Returns([]);
            globalQuestManager
                .Setup(value => value.GetQuestCommunicatorQuestStateTriggers(
                    It.IsAny<ushort>(),
                    It.IsAny<QuestState>()))
                .Returns([]);
            var disableManager = new Mock<IDisableManager>();
            disableManager
                .Setup(value => value.IsDisabled(DisableType.Quest, DeliveredQuestId))
                .Returns(false);
            var manager = new QuestManager(
                player.Object,
                new CharacterModel { Id = 42ul },
                globalQuestManager.Object,
                null,
                disableManager.Object);
            player.SetupGet(value => value.QuestManager).Returns(manager);
            return manager;
        }

        private static CompletionFixture CreateCompletionFixture(
            bool canBeCalledBack,
            bool isCommunicatorReceived)
        {
            IQuestInfo info = CreateQuestInfo(
                DeliveredQuestId,
                canBeCalledBack,
                isCommunicatorReceived);
            var quest = new Mock<IQuest>();
            quest.SetupGet(value => value.Id).Returns(DeliveredQuestId);
            quest.SetupGet(value => value.Info).Returns(info);
            quest.SetupProperty(value => value.State, QuestState.Achieved);
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.AchievementManager).Returns(Mock.Of<ICharacterAchievementManager>());
            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager.Setup(value => value.GetQuestInfo(DeliveredQuestId)).Returns(info);
            var rewardManager = new TestQuestRewardManager();
            var manager = new QuestManager(
                player.Object,
                new CharacterModel { Id = 42ul },
                globalQuestManager.Object,
                rewardManager,
                Mock.Of<IDisableManager>());
            GetActiveQuests(manager).Add(DeliveredQuestId, quest.Object);
            return new CompletionFixture(manager, quest, rewardManager);
        }

        private static QuestEntity CreateQuest(
            IPlayer player,
            IGlobalQuestManager globalQuestManager,
            IScriptManager scriptManager = null)
        {
            IQuestObjectiveInfo objective = QuestObjectiveChecklistTests.CreateObjectiveInfo(
                1u,
                QuestObjectiveType.CollectItem,
                1u);
            IQuestInfo info = QuestObjectiveChecklistTests.CreateQuestInfo(objective);
            var assetManager = new Mock<IAssetManager>();
            assetManager
                .Setup(manager => manager.GetQuestObjectiveTargetIds(It.IsAny<uint>()))
                .Returns(ImmutableList<uint>.Empty);
            return new QuestEntity(
                player,
                info,
                globalQuestManager,
                scriptManager ?? Mock.Of<IScriptManager>(),
                assetManager.Object);
        }

        private static Mock<IPlayer> CreatePlayer(
            uint level = 10u,
            Race race = Race.Human,
            Class @class = Class.Warrior,
            Faction faction = Faction.Exile,
            bool hasReputation = true,
            float reputationAmount = 0f)
        {
            var questManager = new Mock<IQuestManager>();
            var reputationManager = new Mock<IReputationManager>();
            if (hasReputation)
            {
                var reputation = new Mock<IReputation>();
                reputation.SetupGet(value => value.Amount).Returns(reputationAmount);
                reputationManager
                    .Setup(value => value.GetReputation(It.IsAny<Faction>()))
                    .Returns(reputation.Object);
            }
            else
                reputationManager.Setup(value => value.GetReputation(It.IsAny<Faction>())).Returns((IReputation)null);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Level).Returns(level);
            player.SetupGet(value => value.Race).Returns(race);
            player.SetupGet(value => value.Class).Returns(@class);
            player.SetupGet(value => value.Faction1).Returns(faction);
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            player.SetupGet(value => value.ReputationManager).Returns(reputationManager.Object);
            return player;
        }

        private static CommunicatorMessagesEntry CreateEntry(uint id)
        {
            return new CommunicatorMessagesEntry
            {
                Id     = id,
                Quests = [],
                States = []
            };
        }

        private static CommunicatorMessagesEntry CreateTriggerEntry(uint id, uint questId)
        {
            CommunicatorMessagesEntry entry = CreateEntry(id);
            entry.Quests = [questId];
            entry.States = [(uint)QuestState.Achieved];
            return entry;
        }

        private static IReadOnlyDictionary<ushort, IQuestInfo> CreateQuestStore(params ushort[] questIds)
        {
            return questIds.ToDictionary(questId => questId, questId => CreateQuestInfo(questId));
        }

        private static IQuestInfo CreateQuestInfo(
            ushort questId,
            bool canBeCalledBack = false,
            bool isCommunicatorReceived = false,
            bool isContract = false)
        {
            var info = new Mock<IQuestInfo>();
            info.SetupGet(value => value.Entry).Returns(new Quest2Entry
            {
                Id               = questId,
                PushedItemIds    = [],
                PushedItemCounts = []
            });
            info.SetupGet(value => value.Objectives).Returns(ImmutableList<IQuestObjectiveInfo>.Empty);
            info.SetupGet(value => value.PrerequisiteQuests).Returns(ImmutableList<Quest2Entry>.Empty);
            info.Setup(value => value.CanBeCalledBack()).Returns(canBeCalledBack);
            info.Setup(value => value.IsCommunicatorReceived()).Returns(isCommunicatorReceived);
            info.Setup(value => value.IsContract()).Returns(isContract);
            return info.Object;
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

        private sealed record CompletionFixture(
            QuestManager Manager,
            Mock<IQuest> Quest,
            TestQuestRewardManager RewardManager);

        private sealed class TestQuestRewardManager : IQuestRewardManager
        {
            private readonly QuestRewardPlan plan = new([], [], [], [], 0u);

            public int ApplyCount { get; private set; }

            public bool TryCreatePlan(
                IQuestInfo info,
                ushort selectedRewardId,
                out QuestRewardPlan rewardPlan)
            {
                rewardPlan = plan;
                return true;
            }

            public bool TryApply(QuestRewardPlan rewardPlan)
            {
                ApplyCount++;
                return true;
            }
        }
    }
}
