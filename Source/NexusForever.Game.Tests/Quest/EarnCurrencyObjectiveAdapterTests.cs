using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.Game.Tests.Quest
{
    public sealed class EarnCurrencyObjectiveAdapterTests
    {
        private const ulong CharacterId = 42ul;
        private const ulong PrestigeCap = 12_000ul;

        [Fact]
        public void CurrencyAddAmount_ReportsOneCommittedDelta()
        {
            CurrencyFixture fixture = CreateFixture();

            fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, 25ul);

            Assert.Equal(25ul, GetAmount(fixture.Manager, CurrencyType.Renown));
            VerifyEarned(fixture.QuestManager, CurrencyType.Renown, 25u);
        }

        [Fact]
        public void CurrencyAddAmount_CappedGrantReportsOnlyHeadroomAndSuppressesNoChange()
        {
            CurrencyFixture fixture = CreateFixture();
            fixture.Manager.CurrencyAddAmount(CurrencyType.Prestige, PrestigeCap - 10ul);
            ClearRecordedCalls(fixture);

            fixture.Manager.CurrencyAddAmount(CurrencyType.Prestige, ulong.MaxValue, true);

            Assert.Equal(PrestigeCap, GetAmount(fixture.Manager, CurrencyType.Prestige));
            VerifyEarned(fixture.QuestManager, CurrencyType.Prestige, 10u);
            fixture.Session.Verify(
                session => session.EnqueueMessageEncrypted(It.Is<ServerChannelUpdateLoot>(message =>
                    message.CurrencyId == CurrencyType.Prestige && message.Amount == 10ul)),
                Times.Once);

            ClearRecordedCalls(fixture);
            fixture.Manager.CurrencyAddAmount(CurrencyType.Prestige, 1ul);

            Assert.Equal(PrestigeCap, GetAmount(fixture.Manager, CurrencyType.Prestige));
            VerifyNoObjectiveUpdate(fixture.QuestManager);
        }

        [Fact]
        public void CurrencyAddAmount_ZeroAmountDoesNotReportProgress()
        {
            CurrencyFixture fixture = CreateFixture();

            fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, 0ul);

            Assert.Equal(0ul, GetAmount(fixture.Manager, CurrencyType.Renown));
            VerifyNoObjectiveUpdate(fixture.QuestManager);
        }

        [Fact]
        public void CurrencyAddAmount_LargeCommittedDeltaClampsQuestProgress()
        {
            CurrencyFixture fixture = CreateFixture();
            ulong amount = (ulong)uint.MaxValue + 25ul;

            fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, amount);

            Assert.Equal(amount, GetAmount(fixture.Manager, CurrencyType.Renown));
            VerifyEarned(fixture.QuestManager, CurrencyType.Renown, uint.MaxValue);
        }

        [Fact]
        public void CurrencyAddAmount_UncappedOverflowFailsBeforeMutationOrNotification()
        {
            CurrencyFixture fixture = CreateFixture();
            fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, 1ul);
            ClearRecordedCalls(fixture);

            Assert.Throws<OverflowException>(() =>
                fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, ulong.MaxValue, true));

            Assert.Equal(1ul, GetAmount(fixture.Manager, CurrencyType.Renown));
            fixture.Session.Verify(
                session => session.EnqueueMessageEncrypted(It.IsAny<ServerChannelUpdateLoot>()),
                Times.Never);
            fixture.Session.Verify(
                session => session.EnqueueMessageEncrypted(It.IsAny<ServerCombatReward>()),
                Times.Never);
            VerifyNoObjectiveUpdate(fixture.QuestManager);
        }

        [Fact]
        public void CurrencyAddAmount_LootPacketFailureBeforeMutationDoesNotReportProgress()
        {
            CurrencyFixture fixture = CreateFixture();
            var expected = new InvalidOperationException("loot packet failure");
            fixture.Session
                .Setup(session => session.EnqueueMessageEncrypted(It.IsAny<ServerChannelUpdateLoot>()))
                .Throws(expected);

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                fixture.Manager.CurrencyAddAmount(CurrencyType.Prestige, 5ul, true));

            Assert.Same(expected, actual);
            Assert.Equal(0ul, GetAmount(fixture.Manager, CurrencyType.Prestige));
            VerifyNoObjectiveUpdate(fixture.QuestManager);
        }

        [Fact]
        public void CurrencyAddAmount_CombatRewardFailureAfterMutationReportsProgressAndPreservesException()
        {
            CurrencyFixture fixture = CreateFixture();
            var expected = new InvalidOperationException("combat reward packet failure");
            fixture.Session
                .Setup(session => session.EnqueueMessageEncrypted(It.IsAny<ServerCombatReward>()))
                .Throws(expected);

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, 7ul, true));

            Assert.Same(expected, actual);
            Assert.Equal(7ul, GetAmount(fixture.Manager, CurrencyType.Renown));
            VerifyEarned(fixture.QuestManager, CurrencyType.Renown, 7u);
        }

        [Fact]
        public void CurrencyAddAmount_QuestUpdateFailureDoesNotUndoOrFailGrant()
        {
            CurrencyFixture fixture = CreateFixture();
            fixture.QuestManager
                .Setup(manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.EarnCurrency,
                    (uint)CurrencyType.Renown,
                    9u))
                .Throws<InvalidOperationException>();

            Exception exception = Record.Exception(() =>
                fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, 9ul));

            Assert.Null(exception);
            Assert.Equal(9ul, GetAmount(fixture.Manager, CurrencyType.Renown));
            VerifyEarned(fixture.QuestManager, CurrencyType.Renown, 9u);
        }

        [Fact]
        public void CurrencySubtractAmount_DoesNotReportEarnedCurrency()
        {
            CurrencyFixture fixture = CreateFixture();
            fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, 20ul);
            ClearRecordedCalls(fixture);

            fixture.Manager.CurrencySubtractAmount(CurrencyType.Renown, 5ul);

            Assert.Equal(15ul, GetAmount(fixture.Manager, CurrencyType.Renown));
            VerifyNoObjectiveUpdate(fixture.QuestManager);
        }

        [Fact]
        public void CurrencyAddAmount_MissingQuestManagerDoesNotFailGrant()
        {
            CurrencyFixture fixture = CreateFixture(questManagerAvailable: false);

            Exception exception = Record.Exception(() =>
                fixture.Manager.CurrencyAddAmount(CurrencyType.Renown, 3ul));

            Assert.Null(exception);
            Assert.Equal(3ul, GetAmount(fixture.Manager, CurrencyType.Renown));
            VerifyNoObjectiveUpdate(fixture.QuestManager);
        }

        private static CurrencyFixture CreateFixture(bool questManagerAvailable = true)
        {
            GameTable<CurrencyTypeEntry> currencyTable = CreateGameTable(
                new CurrencyTypeEntry
                {
                    Id = (uint)CurrencyType.Renown,
                    CapAmount = 0ul
                },
                new CurrencyTypeEntry
                {
                    Id = (uint)CurrencyType.Prestige,
                    CapAmount = PrestigeCap
                });
            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager.SetupGet(manager => manager.CurrencyType).Returns(currencyTable);

            var session = new Mock<IGameSession>();
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(instance => instance.CharacterId).Returns(CharacterId);
            player.SetupGet(instance => instance.Session).Returns(session.Object);
            player
                .SetupGet(instance => instance.QuestManager)
                .Returns(questManagerAvailable ? questManager.Object : null);

            var manager = new CurrencyManager(gameTableManager.Object);
            manager.Initialise(player.Object, new CharacterModel
            {
                Id = CharacterId
            });

            return new CurrencyFixture(manager, session, questManager);
        }

        private static ulong GetAmount(CurrencyManager manager, CurrencyType currencyType)
        {
            return Assert.Single(manager, currency => currency.Id == currencyType).Amount;
        }

        private static void ClearRecordedCalls(CurrencyFixture fixture)
        {
            fixture.Session.Invocations.Clear();
            fixture.QuestManager.Invocations.Clear();
        }

        private static void VerifyEarned(
            Mock<IQuestManager> questManager,
            CurrencyType currencyType,
            uint progress)
        {
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.EarnCurrency,
                    (uint)currencyType,
                    progress),
                Times.Once);
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Once);
        }

        private static void VerifyNoObjectiveUpdate(Mock<IQuestManager> questManager)
        {
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int i = 0; i < entries.Length; i++)
                lookup[(uint)idField.GetValue(entries[i])] = i;

            typeof(GameTable<T>).GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(table, lookup);
            typeof(GameTable<T>).GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(table,
                new GameTableHeader
                {
                    MaxId = maximumId + 1ul
                });
            return table;
        }

        private sealed record CurrencyFixture(
            CurrencyManager Manager,
            Mock<IGameSession> Session,
            Mock<IQuestManager> QuestManager);
    }
}
