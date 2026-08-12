using System.Reflection;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Tests.Quest
{
    public sealed class QuestIgnoreTests
    {
        private const ushort QuestId = 100;
        private const ulong CharacterId = 42ul;

        [Theory]
        [InlineData(QuestState.Mentioned, true, QuestState.Ignored, 1)]
        [InlineData(QuestState.Ignored, false, QuestState.Mentioned, 1)]
        [InlineData(QuestState.Mentioned, false, QuestState.Mentioned, 0)]
        [InlineData(QuestState.Ignored, true, QuestState.Ignored, 0)]
        public void InactiveNativeTransitionOrReplayPreservesExactOwnershipAndObjectives(
            QuestState initialState,
            bool ignored,
            QuestState expectedState,
            int expectedStateSets)
        {
            QuestIgnoreFixture fixture = CreateFixture();
            QuestDouble quest = CreateQuest(initialState);
            GetStore(fixture.Manager, "inactiveQuests").Add(QuestId, quest.Quest.Object);

            fixture.Manager.QuestIgnore(QuestId, ignored);

            Assert.Equal(expectedState, quest.State());
            Assert.Equal(expectedStateSets, quest.StateSets());
            Assert.Same(quest.Quest.Object, Assert.Single(GetStore(fixture.Manager, "inactiveQuests")).Value);
            Assert.Empty(GetStore(fixture.Manager, "activeQuests"));
            Assert.Empty(GetStore(fixture.Manager, "completedQuests"));
            quest.Quest.Verify(value => value.EnqueueDelete(It.IsAny<bool>()), Times.Never);
            quest.Objective.VerifySet(value => value.Progress = It.IsAny<uint>(), Times.Never);
        }

        [Fact]
        public void AbandonedHideRehomesAndCancelsDeleteBeforeCommittedStateNotification()
        {
            QuestIgnoreFixture fixture = CreateFixture();
            QuestDouble quest = null;
            quest = CreateQuest(
                QuestState.Abandoned,
                pendingDelete: true,
                onStateSet: state =>
                {
                    Assert.Equal(QuestState.Ignored, state);
                    Assert.False(quest.PendingDelete());
                    Assert.Empty(GetStore(fixture.Manager, "activeQuests"));
                    Assert.Same(
                        quest.Quest.Object,
                        Assert.Single(GetStore(fixture.Manager, "inactiveQuests")).Value);
                });
            GetStore(fixture.Manager, "activeQuests").Add(QuestId, quest.Quest.Object);

            fixture.Manager.QuestIgnore(QuestId, true);

            Assert.Equal(QuestState.Ignored, quest.State());
            Assert.False(quest.PendingDelete());
            Assert.Empty(GetStore(fixture.Manager, "activeQuests"));
            Assert.Same(quest.Quest.Object, Assert.Single(GetStore(fixture.Manager, "inactiveQuests")).Value);
            quest.Quest.Verify(value => value.EnqueueDelete(false), Times.Once);
            quest.Objective.VerifySet(value => value.Progress = It.IsAny<uint>(), Times.Never);
        }

        [Fact]
        public void AbandonedFalseIsIdempotentAfterExactOwnershipValidation()
        {
            QuestIgnoreFixture fixture = CreateFixture();
            QuestDouble quest = CreateQuest(QuestState.Abandoned, pendingDelete: true);
            GetStore(fixture.Manager, "activeQuests").Add(QuestId, quest.Quest.Object);

            fixture.Manager.QuestIgnore(QuestId, false);

            Assert.Equal(QuestState.Abandoned, quest.State());
            Assert.True(quest.PendingDelete());
            Assert.Same(quest.Quest.Object, Assert.Single(GetStore(fixture.Manager, "activeQuests")).Value);
            Assert.Empty(GetStore(fixture.Manager, "inactiveQuests"));
            Assert.Equal(0, quest.StateSets());
            quest.Quest.Verify(value => value.EnqueueDelete(It.IsAny<bool>()), Times.Never);
            quest.Objective.VerifySet(value => value.Progress = It.IsAny<uint>(), Times.Never);
        }

        public static IEnumerable<object[]> InvalidStates()
        {
            foreach (QuestState state in new[]
            {
                QuestState.Unknown,
                QuestState.Accepted,
                QuestState.Achieved,
                QuestState.Completed,
                QuestState.Botched
            })
            {
                yield return [state, false];
                yield return [state, true];
            }
        }

        [Theory]
        [MemberData(nameof(InvalidStates))]
        public void InvalidExistingStateSilentlyPreservesStateOwnershipAndObjectives(
            QuestState state,
            bool ignored)
        {
            QuestIgnoreFixture fixture = CreateFixture();
            QuestDouble quest = CreateQuest(state);
            string home = state switch
            {
                QuestState.Accepted or QuestState.Achieved => "activeQuests",
                QuestState.Completed                       => "completedQuests",
                _                                          => "inactiveQuests"
            };
            GetStore(fixture.Manager, home).Add(QuestId, quest.Quest.Object);

            Exception exception = Record.Exception(() => fixture.Manager.QuestIgnore(QuestId, ignored));

            Assert.Null(exception);
            Assert.Equal(state, quest.State());
            Assert.Same(quest.Quest.Object, Assert.Single(GetStore(fixture.Manager, home)).Value);
            Assert.Equal(0, quest.StateSets());
            quest.Quest.Verify(value => value.EnqueueDelete(It.IsAny<bool>()), Times.Never);
            quest.Objective.VerifySet(value => value.Progress = It.IsAny<uint>(), Times.Never);
        }

        [Theory]
        [InlineData("mentioned-active")]
        [InlineData("ignored-alias")]
        [InlineData("abandoned-no-delete")]
        [InlineData("abandoned-alias")]
        [InlineData("abandoned-inactive")]
        public void InvalidOwnershipSilentlyRejectsBeforeReplayOrMutation(string condition)
        {
            QuestIgnoreFixture fixture = CreateFixture();
            QuestState state = condition.StartsWith("abandoned", StringComparison.Ordinal)
                ? QuestState.Abandoned
                : condition.StartsWith("ignored", StringComparison.Ordinal)
                    ? QuestState.Ignored
                    : QuestState.Mentioned;
            bool pendingDelete = state == QuestState.Abandoned && condition != "abandoned-no-delete";
            QuestDouble quest = CreateQuest(state, pendingDelete);
            bool ignored = state != QuestState.Ignored;

            switch (condition)
            {
                case "mentioned-active":
                    GetStore(fixture.Manager, "activeQuests").Add(QuestId, quest.Quest.Object);
                    break;
                case "ignored-alias":
                    GetStore(fixture.Manager, "activeQuests").Add(QuestId, quest.Quest.Object);
                    GetStore(fixture.Manager, "inactiveQuests").Add(QuestId, quest.Quest.Object);
                    break;
                case "abandoned-no-delete":
                    GetStore(fixture.Manager, "activeQuests").Add(QuestId, quest.Quest.Object);
                    break;
                case "abandoned-alias":
                    GetStore(fixture.Manager, "activeQuests").Add(QuestId, quest.Quest.Object);
                    GetStore(fixture.Manager, "completedQuests").Add(QuestId, quest.Quest.Object);
                    break;
                case "abandoned-inactive":
                    GetStore(fixture.Manager, "inactiveQuests").Add(QuestId, quest.Quest.Object);
                    break;
            }

            Exception exception = Record.Exception(() => fixture.Manager.QuestIgnore(QuestId, ignored));

            Assert.Null(exception);
            Assert.Equal(state, quest.State());
            Assert.Equal(pendingDelete, quest.PendingDelete());
            Assert.Equal(0, quest.StateSets());
            quest.Quest.Verify(value => value.EnqueueDelete(It.IsAny<bool>()), Times.Never);
            quest.Objective.VerifySet(value => value.Progress = It.IsAny<uint>(), Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingValidQuestSilentlyRejectsWithoutConstruction(bool ignored)
        {
            QuestIgnoreFixture fixture = CreateFixture();

            Exception exception = Record.Exception(() => fixture.Manager.QuestIgnore(QuestId, ignored));

            Assert.Null(exception);
            Assert.Equal(0, fixture.FactoryCalls());
            Assert.Empty(GetStore(fixture.Manager, "activeQuests"));
            Assert.Empty(GetStore(fixture.Manager, "inactiveQuests"));
            Assert.Empty(GetStore(fixture.Manager, "completedQuests"));
        }

        [Theory]
        [InlineData("missing-info")]
        [InlineData("missing-entry")]
        [InlineData("mismatched-id")]
        public void InvalidStaticMetadataThrowsBeforeQuestResolution(string condition)
        {
            QuestIgnoreFixture fixture = CreateFixture(condition);

            Assert.Throws<ArgumentException>(() => fixture.Manager.QuestIgnore(QuestId, true));

            fixture.GlobalQuestManager.Verify(value => value.GetQuestInfo(QuestId), Times.Once);
            Assert.Equal(0, fixture.FactoryCalls());
        }

        [Fact]
        public void CommittedStatePublicationFailureRetainsRehomeAndDeleteCancellation()
        {
            QuestIgnoreFixture fixture = CreateFixture();
            QuestDouble quest = CreateQuest(
                QuestState.Abandoned,
                pendingDelete: true,
                throwAfterStateSet: true);
            GetStore(fixture.Manager, "activeQuests").Add(QuestId, quest.Quest.Object);

            Exception exception = Record.Exception(() => fixture.Manager.QuestIgnore(QuestId, true));

            Assert.Null(exception);
            Assert.Equal(QuestState.Ignored, quest.State());
            Assert.False(quest.PendingDelete());
            Assert.Empty(GetStore(fixture.Manager, "activeQuests"));
            Assert.Same(quest.Quest.Object, Assert.Single(GetStore(fixture.Manager, "inactiveQuests")).Value);
            quest.Quest.Verify(value => value.EnqueueDelete(false), Times.Once);
            quest.Objective.VerifySet(value => value.Progress = It.IsAny<uint>(), Times.Never);
        }

        private static QuestIgnoreFixture CreateFixture(string metadataCondition = null)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);

            var info = new Mock<IQuestInfo>();
            if (metadataCondition != "missing-entry")
            {
                info.SetupGet(value => value.Entry).Returns(new Quest2Entry
                {
                    Id = metadataCondition == "mismatched-id" ? QuestId + 1u : QuestId
                });
            }

            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager
                .Setup(value => value.GetQuestInfo(QuestId))
                .Returns(metadataCondition == "missing-info" ? null : info.Object);

            int factoryCalls = 0;
            var manager = new QuestManager(
                player.Object,
                new CharacterModel { Id = CharacterId },
                globalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>(),
                questFactory: _ =>
                {
                    factoryCalls++;
                    return Mock.Of<IQuest>();
                });

            return new QuestIgnoreFixture(
                manager,
                globalQuestManager,
                () => factoryCalls);
        }

        private static QuestDouble CreateQuest(
            QuestState initialState,
            bool pendingDelete = false,
            Action<QuestState> onStateSet = null,
            bool throwAfterStateSet = false)
        {
            QuestState state = initialState;
            bool delete = pendingDelete;
            int stateSets = 0;
            var objective = new Mock<IQuestObjective>();
            objective.SetupProperty(value => value.Progress, 37u);

            var quest = new Mock<IQuest>();
            quest.SetupGet(value => value.Id).Returns(QuestId);
            quest.SetupGet(value => value.State).Returns(() => state);
            quest.SetupGet(value => value.PendingDelete).Returns(() => delete);
            quest
                .Setup(value => value.EnqueueDelete(It.IsAny<bool>()))
                .Callback<bool>(value => delete = value);
            quest
                .Setup(value => value.GetEnumerator())
                .Returns(() => new[] { objective.Object }.AsEnumerable().GetEnumerator());

            void SetState(QuestState value)
            {
                state = value;
                stateSets++;
                onStateSet?.Invoke(value);
                if (throwAfterStateSet)
                    throw new InvalidOperationException("Test quest state publication failure.");
            }

            quest
                .SetupSet(value => value.State = QuestState.Mentioned)
                .Callback(() => SetState(QuestState.Mentioned));
            quest
                .SetupSet(value => value.State = QuestState.Ignored)
                .Callback(() => SetState(QuestState.Ignored));

            return new QuestDouble(
                quest,
                objective,
                () => state,
                () => delete,
                () => stateSets);
        }

        private static Dictionary<ushort, IQuest> GetStore(QuestManager manager, string name)
        {
            FieldInfo field = typeof(QuestManager).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (Dictionary<ushort, IQuest>)field.GetValue(manager);
        }

        private sealed record QuestIgnoreFixture(
            QuestManager Manager,
            Mock<IGlobalQuestManager> GlobalQuestManager,
            Func<int> FactoryCalls);

        private sealed record QuestDouble(
            Mock<IQuest> Quest,
            Mock<IQuestObjective> Objective,
            Func<QuestState> State,
            Func<bool> PendingDelete,
            Func<int> StateSets);
    }
}
