using System.Collections.Concurrent;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Currency;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Loot;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Game.Static.Quest;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Static;
using Moq;

namespace NexusForever.Game.Tests.Loot
{
    public class LootInstanceItemTests
    {
        [Fact]
        public void Constructor_AssignsUniqueIds()
        {
            var item1 = new LootInstanceItem(100, LootItemType.StaticItem, 1);
            var item2 = new LootInstanceItem(200, LootItemType.StaticItem, 1);

            Assert.NotEqual(item1.Id, item2.Id);
        }

        [Fact]
        public void Constructor_SetsProperties()
        {
            var item = new LootInstanceItem(42, LootItemType.Cash, 500);

            Assert.Equal(42u, item.StaticId);
            Assert.Equal(LootItemType.Cash, item.Type);
            Assert.Equal(500u, item.Amount);
            Assert.False(item.Delivered);
        }

        [Fact]
        public void SetWinner_SetsWinnerFields()
        {
            var item = new LootInstanceItem(100, LootItemType.StaticItem, 1);
            item.SetWinner(12345uL, 99u);

            Assert.Equal(12345uL, item.WinnerCharacterId);
            Assert.Equal(99u, item.WinnerGuid);
        }

        [Fact]
        public void SetWinner_SameCharacterAfterReconnect_RefreshesUnitGuid()
        {
            var item = new LootInstanceItem(100, LootItemType.StaticItem, 1);
            item.SetWinner(12345ul, 99u);
            item.SetWinner(12345ul, 100u);

            Assert.Equal(12345ul, item.WinnerCharacterId);
            Assert.Equal(100u, item.WinnerGuid);
        }

        [Fact]
        public void SetWinner_DifferentCharacter_ThrowsInvalidOperationException()
        {
            var item = new LootInstanceItem(100, LootItemType.StaticItem, 1);
            item.SetWinner(12345ul, 99u);

            Assert.Throws<InvalidOperationException>(() => item.SetWinner(54321ul, 100u));
        }

        [Fact]
        public void AddToAmount_IncreasesAmount()
        {
            var item = new LootInstanceItem(100, LootItemType.StaticItem, 5);
            item.AddToAmount(3);

            Assert.Equal(8u, item.Amount);
        }

        [Fact]
        public void DeliverItem_StaticItem_CallsInventoryCreate()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(999, LootItemType.StaticItem, 3);
            item.SetWinner(player.CharacterId, player.Guid);

            bool delivered = item.DeliverItem(player);

            Assert.True(delivered);
            mocks.Inventory.Verify(
                i => i.TryItemCreate(
                    InventoryLocation.Inventory,
                    999u,
                    3u,
                    out It.Ref<uint>.IsAny,
                    ItemUpdateReason.Loot,
                    0u),
                Times.Once);
            Assert.True(item.Delivered);
        }

        [Fact]
        public void DeliverItem_StaticItemWithoutCapacity_RemainsClaimable()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(999, LootItemType.StaticItem, 3);
            item.SetWinner(player.CharacterId, player.Guid);
            mocks.Inventory.Setup(i => i.TryItemCreate(
                    InventoryLocation.Inventory,
                    999u,
                    3u,
                    out It.Ref<uint>.IsAny,
                    ItemUpdateReason.Loot,
                    0u))
                .Returns(false);

            bool delivered = item.DeliverItem(player);

            Assert.False(delivered);
            Assert.False(item.Delivered);
            mocks.Inventory.Verify(i => i.ItemCreate(
                It.IsAny<InventoryLocation>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<ItemUpdateReason>(),
                It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void DeliverItem_Cash_CallsCurrencyAdd()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(1, LootItemType.Cash, 500);
            item.SetWinner(player.CharacterId, player.Guid);

            bool delivered = item.DeliverItem(player);

            Assert.True(delivered);
            mocks.Currency.Verify(
                c => c.CurrencyAddAmount((CurrencyType)1, 500u, true),
                Times.Once);
            Assert.True(item.Delivered);
        }

        [Fact]
        public void DeliverItem_AccountCurrency_CallsAccountCurrencyAdd()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(6, LootItemType.AccountCurrency, 25);
            item.SetWinner(player.CharacterId, player.Guid);

            bool delivered = item.DeliverItem(player);

            Assert.True(delivered);
            mocks.AccountCurrency.Verify(
                c => c.CurrencyAddAmount((AccountCurrencyType)6, 25u, 0uL),
                Times.Once);
            Assert.True(item.Delivered);
        }

        [Fact]
        public void DeliverItem_VirtualItem_CallsQuestObjectiveUpdate()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(555, LootItemType.VirtualItem, 2);
            item.SetWinner(player.CharacterId, player.Guid);

            bool delivered = item.DeliverItem(player);

            Assert.True(delivered);
            mocks.Quest.Verify(
                q => q.ObjectiveUpdate(QuestObjectiveType.VirtualCollect, 555u, 2u),
                Times.Once);
            Assert.True(item.Delivered);
        }

        [Fact]
        public void DeliverItem_AlreadyDelivered_DoesNothing()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(999, LootItemType.StaticItem, 1);
            item.SetWinner(player.CharacterId, player.Guid);

            bool first = item.DeliverItem(player);
            bool second = item.DeliverItem(player);

            Assert.True(first);
            Assert.False(second);
            mocks.Inventory.Verify(
                i => i.TryItemCreate(
                    It.IsAny<InventoryLocation>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    out It.Ref<uint>.IsAny,
                    It.IsAny<ItemUpdateReason>(),
                    It.IsAny<uint>()),
                Times.Once);
        }

        [Fact]
        public void DeliverItem_ConcurrentClaims_DeliversOnlyOnce()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(1, LootItemType.Cash, 500);
            item.SetWinner(player.CharacterId, player.Guid);
            var results = new ConcurrentBag<bool>();

            Parallel.For(0, 64, _ => results.Add(item.DeliverItem(player)));

            Assert.Single(results, result => result);
            mocks.Currency.Verify(
                c => c.CurrencyAddAmount((CurrencyType)1, 500u, true),
                Times.Once);
        }

        [Fact]
        public void Constructor_UnsupportedType_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LootInstanceItem(1, unchecked((LootItemType)uint.MaxValue), 1));
        }

        [Fact]
        public void DeliverItem_RewardThrowsAfterMutation_DoesNotRetry()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(1, LootItemType.Cash, 500);
            item.SetWinner(player.CharacterId, player.Guid);
            mocks.Currency.Setup(c => c.CurrencyAddAmount((CurrencyType)1, 500u, true))
                .Throws(new InvalidOperationException("Packet enqueue failed after mutation."));

            Assert.Throws<InvalidOperationException>(() => item.DeliverItem(player));

            Assert.True(item.Delivered);
            Assert.False(item.DeliverItem(player));
            mocks.Currency.Verify(c => c.CurrencyAddAmount((CurrencyType)1, 500u, true), Times.Once);
        }

        [Fact]
        public void DeliverItem_WrongWinner_DoesNotDeliver()
        {
            var (player, mocks) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(1, LootItemType.Cash, 500);
            item.SetWinner(999ul, 999u);

            bool delivered = item.DeliverItem(player);

            Assert.False(delivered);
            mocks.Currency.Verify(c => c.CurrencyAddAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void Build_ReturnsCorrectLootItem()
        {
            var item = new LootInstanceItem(42, LootItemType.StaticItem, 5);

            var network = item.Build();

            Assert.Equal(item.Id, network.LootUnitId);
            Assert.Equal(LootItemType.StaticItem, network.Type);
            Assert.Equal(42u, network.ItemId);
            Assert.Equal(5u, network.Amount);
            Assert.True(network.CanLoot);
        }

        [Fact]
        public void Build_AfterDelivery_CanLootIsFalse()
        {
            var (player, _) = CreateMockPlayerWithMocks();
            var item = new LootInstanceItem(42, LootItemType.StaticItem, 5);
            item.SetWinner(player.CharacterId, player.Guid);

            item.DeliverItem(player);
            var network = item.Build();

            Assert.False(network.CanLoot);
        }

        [Fact]
        public void BuildForAccountCurrency_SplitsIntoMultipleItems()
        {
            var item = new LootInstanceItem(6, LootItemType.AccountCurrency, 100);

            var networkItems = item.BuildForAccountCurrency();

            Assert.True(networkItems.Count <= 50);
            Assert.Equal(100u, (uint)networkItems.Sum(i => i.Amount));
            Assert.All(networkItems, i => Assert.True(i.Explosion));
        }

        [Fact]
        public void BuildForAccountCurrency_SmallAmount_SingleItem()
        {
            var item = new LootInstanceItem(6, LootItemType.AccountCurrency, 1);

            var networkItems = item.BuildForAccountCurrency();

            Assert.Single(networkItems);
            Assert.Equal(1u, networkItems[0].Amount);
        }

        private record MockSet(
            Mock<IInventory> Inventory,
            Mock<ICurrencyManager> Currency,
            Mock<IAccountCurrencyManager> AccountCurrency,
            Mock<IQuestManager> Quest);

        private static (IPlayer Player, MockSet Mocks) CreateMockPlayerWithMocks(
            ulong characterId = 1ul,
            uint guid = 1u)
        {
            var mockSession = new Mock<IGameSession>();
            var mockInventory = new Mock<IInventory>();
            var mockCurrency = new Mock<ICurrencyManager>();
            var mockQuest = new Mock<IQuestManager>();
            var mockAccount = new Mock<IAccount>();
            var mockAccountCurrency = new Mock<IAccountCurrencyManager>();

            mockAccount.Setup(a => a.CurrencyManager).Returns(mockAccountCurrency.Object);
            mockInventory.Setup(i => i.TryItemCreate(
                    It.IsAny<InventoryLocation>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    out It.Ref<uint>.IsAny,
                    It.IsAny<ItemUpdateReason>(),
                    It.IsAny<uint>()))
                .Returns(true);

            var mockPlayer = new Mock<IPlayer>();
            mockPlayer.Setup(p => p.CharacterId).Returns(characterId);
            mockPlayer.Setup(p => p.Guid).Returns(guid);
            mockPlayer.Setup(p => p.Session).Returns(mockSession.Object);
            mockPlayer.Setup(p => p.Inventory).Returns(mockInventory.Object);
            mockPlayer.Setup(p => p.CurrencyManager).Returns(mockCurrency.Object);
            mockPlayer.Setup(p => p.QuestManager).Returns(mockQuest.Object);
            mockPlayer.Setup(p => p.Account).Returns(mockAccount.Object);

            return (mockPlayer.Object, new MockSet(mockInventory, mockCurrency, mockAccountCurrency, mockQuest));
        }
    }
}
