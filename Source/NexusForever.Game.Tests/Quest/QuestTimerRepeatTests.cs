using System.Collections.Immutable;
using System.Reflection;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Achievement;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Entity;
using NexusForever.Game.Quest;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Achievement;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Script;
using QuestEntity = NexusForever.Game.Quest.Quest;

namespace NexusForever.Game.Tests.Quest
{
    public sealed class QuestTimerTests
    {
        [Fact]
        public void LoadedTimer_UsesPersistedMilliseconds()
        {
            QuestFixture fixture = CreateLoadedQuest(QuestState.Accepted, 1_500u, 5_000u);

            fixture.Quest.Update(0.5d);

            Assert.Equal(1_000u, fixture.Quest.Timer);
            Assert.Equal(QuestState.Accepted, fixture.Quest.State);
        }

        [Fact]
        public void LoadedTimer_ClampsValueAboveStaticMaximum()
        {
            QuestFixture fixture = CreateLoadedQuest(QuestState.Accepted, 9_000u, 5_000u);

            Assert.Equal(5_000u, fixture.Quest.Timer);

            fixture.Quest.Update(1d);
            Assert.Equal(4_000u, fixture.Quest.Timer);
        }

        [Fact]
        public void LoadedTimer_WithoutTimedQuestMetadataIsCleared()
        {
            QuestFixture fixture = CreateLoadedQuest(QuestState.Accepted, 1_500u, 0u);

            Assert.Null(fixture.Quest.Timer);

            fixture.Quest.Update(1d);
            Assert.Equal(QuestState.Accepted, fixture.Quest.State);
        }

        [Fact]
        public void LoadedTimedQuest_WithoutPersistedTimerFailsClosed()
        {
            QuestFixture fixture = CreateLoadedQuest(QuestState.Accepted, null, 5_000u);

            Assert.Null(fixture.Quest.Timer);
            Assert.Equal(QuestState.Botched, fixture.Quest.State);
        }

        [Fact]
        public void AchievedTimedQuest_DoesNotExpireAfterLoading()
        {
            QuestFixture fixture = CreateLoadedQuest(QuestState.Achieved, 1_500u, 5_000u);

            fixture.Quest.Update(60d);

            Assert.Equal(1_500u, fixture.Quest.Timer);
            Assert.Equal(QuestState.Achieved, fixture.Quest.State);
        }

        [Fact]
        public void AcceptedTimer_ExpiresAndBotchesExactlyOnce()
        {
            QuestFixture fixture = CreateLoadedQuest(QuestState.Accepted, 1_500u, 5_000u);

            fixture.Quest.Update(1.5d);
            fixture.Quest.Update(1d);

            Assert.Equal(0u, fixture.Quest.Timer);
            Assert.Equal(QuestState.Botched, fixture.Quest.State);
            Assert.Single(GetMessages<ServerQuestStateChange>(fixture.Session));
        }

        [Fact]
        public void Timer_IgnoresInvalidAndNonPositiveElapsedValues()
        {
            QuestFixture fixture = CreateLoadedQuest(QuestState.Accepted, 1_500u, 5_000u);

            fixture.Quest.Update(double.NaN);
            fixture.Quest.Update(double.PositiveInfinity);
            fixture.Quest.Update(double.NegativeInfinity);
            fixture.Quest.Update(0d);
            fixture.Quest.Update(-1d);

            Assert.Equal(1_500u, fixture.Quest.Timer);
            Assert.Equal(QuestState.Accepted, fixture.Quest.State);
        }

        [Fact]
        public void InitialPacket_UsesPersistedRemainingQuestAndObjectiveMilliseconds()
        {
            QuestFixture fixture = CreateLoadedQuest(
                QuestState.Accepted,
                1_500u,
                5_000u,
                objectiveTimer: 750u);
            var manager = new QuestManager(
                fixture.Player.Object,
                new CharacterModel(),
                fixture.GlobalQuestManager.Object,
                null,
                Mock.Of<IDisableManager>(),
                () => new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc));
            GetQuestDictionary(manager, "activeQuests").Add(fixture.Quest.Id, fixture.Quest);

            manager.SendInitialPackets();

            ServerQuestInit packet = Assert.Single(GetMessages<ServerQuestInit>(fixture.Session));
            ServerQuestInit.QuestActive active = Assert.Single(packet.Active);
            Assert.Equal(1_500u, active.QuestTimeRemaining);
            Assert.Equal(750u, Assert.Single(active.Objectives).TimeRemaining);
        }

        private static QuestFixture CreateLoadedQuest(
            QuestState state,
            uint? timer,
            uint maximumTimer,
            uint? objectiveTimer = null)
        {
            const ushort questId = 100;
            const uint objectiveId = 200u;

            var session = new Mock<IGameSession>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.Session).Returns(session.Object);
            player.SetupGet(value => value.QuestManager).Returns(Mock.Of<IQuestManager>());

            var objectiveInfo = new QuestObjectiveInfo(new QuestObjectiveEntry
            {
                Id    = objectiveId,
                Type  = (uint)QuestObjectiveType.CollectItem,
                Count = 10u
            });
            var questInfo = new Mock<IQuestInfo>();
            questInfo.SetupGet(value => value.Entry).Returns(new Quest2Entry
            {
                Id               = questId,
                MaxTimeAllowedMS = maximumTimer,
                PushedItemIds    = [],
                PushedItemCounts = []
            });
            questInfo.SetupGet(value => value.Objectives).Returns([objectiveInfo]);

            var model = new CharacterQuestModel
            {
                Id      = 42ul,
                QuestId = questId,
                State   = (byte)state,
                Timer   = timer
            };
            model.QuestObjective.Add(new CharacterQuestObjectiveModel
            {
                Id       = 42ul,
                QuestId  = questId,
                Index    = 0,
                Progress = 1u,
                Timer    = objectiveTimer
            });

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
                questInfo.Object,
                model,
                globalQuestManager.Object,
                new Mock<IScriptManager>().Object,
                assetManager.Object);
            return new QuestFixture(quest, player, session, globalQuestManager);
        }

        private static Dictionary<ushort, IQuest> GetQuestDictionary(QuestManager manager, string name)
        {
            FieldInfo field = typeof(QuestManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
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

        private sealed record QuestFixture(
            QuestEntity Quest,
            Mock<IPlayer> Player,
            Mock<IGameSession> Session,
            Mock<IGlobalQuestManager> GlobalQuestManager);
    }

    public sealed class QuestResetCalculatorTests
    {
        public static TheoryData<QuestRepeatPeriod, DateTime, DateTime> ResetCases => new()
        {
            {
                QuestRepeatPeriod.Daily,
                Utc(2026, 8, 10, 9),
                Utc(2026, 8, 10, 10)
            },
            {
                QuestRepeatPeriod.Daily,
                Utc(2026, 8, 10, 10),
                Utc(2026, 8, 11, 10)
            },
            {
                QuestRepeatPeriod.Weekly,
                Utc(2026, 8, 10, 12),
                Utc(2026, 8, 11, 10)
            },
            {
                QuestRepeatPeriod.Weekly,
                Utc(2026, 8, 11, 10),
                Utc(2026, 8, 18, 10)
            },
            {
                QuestRepeatPeriod.Monthly,
                Utc(2026, 12, 31, 23),
                Utc(2027, 1, 1, 10)
            },
            {
                QuestRepeatPeriod.Yearly,
                Utc(2028, 1, 1, 9),
                Utc(2028, 1, 1, 10)
            },
            {
                QuestRepeatPeriod.Yearly,
                Utc(2028, 1, 1, 10),
                Utc(2029, 1, 1, 10)
            }
        };

        [Theory]
        [MemberData(nameof(ResetCases))]
        public void TryCalculateNext_ReturnsStrictlyFutureBoundary(
            QuestRepeatPeriod period,
            DateTime now,
            DateTime expected)
        {
            Assert.True(QuestResetCalculator.TryCalculateNext(period, now, out DateTime actual));
            Assert.Equal(expected, actual);
            Assert.True(actual > now);
        }

        [Fact]
        public void TryCalculateNext_UnknownPeriodFailsClosed()
        {
            Assert.False(QuestResetCalculator.TryCalculateNext(
                (QuestRepeatPeriod)99,
                Utc(2026, 8, 10, 12),
                out DateTime reset));
            Assert.Equal(default, reset);
        }

        [Fact]
        public void GlobalResetUpdate_RecoversFromLongClockJump()
        {
            var manager = new GlobalQuestManager();
            manager.CalculateResetTimes(Utc(2026, 8, 10, 9));

            manager.UpdateResetTimes(Utc(2028, 2, 20, 12));

            Assert.Equal(Utc(2028, 2, 21, 10), manager.NextDailyReset);
            Assert.Equal(Utc(2028, 2, 22, 10), manager.NextWeeklyReset);
        }

        private static DateTime Utc(int year, int month, int day, int hour)
        {
            return new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc);
        }
    }

    public sealed class QuestRepeatSecurityTests
    {
        private const ushort QuestId = 100;

        [Theory]
        [InlineData(QuestRepeatPeriod.Monthly, 2026, 9, 1)]
        [InlineData(QuestRepeatPeriod.Yearly, 2027, 1, 1)]
        public void QuestComplete_AssignsResetFromActualCompletionTime(
            QuestRepeatPeriod repeatPeriod,
            int expectedYear,
            int expectedMonth,
            int expectedDay)
        {
            DateTime now = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            CompletionFixture fixture = CreateCompletionFixture(repeatPeriod, now);

            fixture.Manager.QuestComplete(QuestId, 0, true);

            Assert.Equal(
                new DateTime(expectedYear, expectedMonth, expectedDay, 10, 0, 0, DateTimeKind.Utc),
                fixture.Quest.Object.Reset);
            Assert.Equal(1, fixture.RewardManager.PrepareCount);
            Assert.Equal(1, fixture.RewardManager.ApplyCount);
        }

        [Fact]
        public void QuestComplete_UnknownRepeatPeriodRejectsBeforeRewardPlanning()
        {
            CompletionFixture fixture = CreateCompletionFixture(
                (QuestRepeatPeriod)99,
                new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc));

            Assert.Throws<QuestException>(() => fixture.Manager.QuestComplete(QuestId, 0, true));

            Assert.Equal(0, fixture.RewardManager.PrepareCount);
            Assert.Equal(0, fixture.RewardManager.ApplyCount);
            Assert.Equal(QuestState.Achieved, fixture.Quest.Object.State);
        }

        [Theory]
        [InlineData(QuestRepeatPeriod.Daily)]
        [InlineData((QuestRepeatPeriod)99)]
        public void QuestAdd_CompletedQuestWithMissingOrUnknownResetMetadataFailsClosed(
            QuestRepeatPeriod repeatPeriod)
        {
            CompletionFixture fixture = CreateCompletionFixture(
                repeatPeriod,
                new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
                QuestState.Completed);

            Assert.Throws<QuestException>(() => fixture.Manager.QuestAdd(QuestId, null));
            Assert.Equal(QuestState.Completed, fixture.Quest.Object.State);
        }

        private static CompletionFixture CreateCompletionFixture(
            QuestRepeatPeriod repeatPeriod,
            DateTime now,
            QuestState state = QuestState.Achieved)
        {
            var achievementManager = new Mock<ICharacterAchievementManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(42ul);
            player.SetupGet(value => value.AchievementManager).Returns(achievementManager.Object);

            Quest2Entry entry = new()
            {
                Id                    = QuestId,
                QuestRepeatPeriodEnum = (uint)repeatPeriod,
                PushedItemIds         = [],
                PushedItemCounts      = []
            };
            var info = new Mock<IQuestInfo>();
            info.SetupGet(value => value.Entry).Returns(entry);
            info.Setup(value => value.IsCommunicatorReceived()).Returns(true);

            var quest = new Mock<IQuest>();
            quest.SetupGet(value => value.Id).Returns(QuestId);
            quest.SetupGet(value => value.Info).Returns(info.Object);
            quest.SetupProperty(value => value.State, state);
            quest.SetupProperty(value => value.Reset, null);

            var globalQuestManager = new Mock<IGlobalQuestManager>();
            globalQuestManager.Setup(manager => manager.GetQuestInfo(QuestId)).Returns(info.Object);
            var disableManager = new Mock<IDisableManager>();
            disableManager.Setup(manager => manager.IsDisabled(DisableType.Quest, QuestId)).Returns(false);
            var rewardManager = new TestQuestRewardManager();

            var manager = new QuestManager(
                player.Object,
                new CharacterModel
                {
                    Id = 42ul
                },
                globalQuestManager.Object,
                rewardManager,
                disableManager.Object,
                () => now);
            string dictionaryName = state == QuestState.Completed ? "completedQuests" : "activeQuests";
            GetQuestDictionary(manager, dictionaryName).Add(QuestId, quest.Object);

            return new CompletionFixture(manager, quest, rewardManager);
        }

        private static Dictionary<ushort, IQuest> GetQuestDictionary(QuestManager manager, string name)
        {
            FieldInfo field = typeof(QuestManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return (Dictionary<ushort, IQuest>)field.GetValue(manager);
        }

        private sealed class TestQuestRewardManager : IQuestRewardManager
        {
            private readonly QuestRewardPlan plan = new([], [], [], [], 0u);

            public int PrepareCount { get; private set; }
            public int ApplyCount { get; private set; }

            public bool TryCreatePlan(IQuestInfo info, ushort selectedRewardId, out QuestRewardPlan rewardPlan)
            {
                PrepareCount++;
                rewardPlan = plan;
                return true;
            }

            public bool TryApply(QuestRewardPlan rewardPlan)
            {
                ApplyCount++;
                return true;
            }
        }

        private sealed record CompletionFixture(
            QuestManager Manager,
            Mock<IQuest> Quest,
            TestQuestRewardManager RewardManager);
    }
}
