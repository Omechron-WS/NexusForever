using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Currency;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Loot;
using NexusForever.Game.Static.Loot;
using NexusForever.Network.Session;
using Moq;

namespace NexusForever.Game.Tests.Loot
{
    public class LootInstanceTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            var instance = new LootInstance(42u, LootEntityType.Creature, LooterType.Player);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);

            Assert.Equal(42u, instance.Guid);
            Assert.Equal(LootEntityType.Creature, instance.LootEntityType);
            Assert.Equal(LooterType.Player, instance.LooterType);
            Assert.False(instance.HasExpired);
            Assert.False(instance.Explosion);
        }

        [Fact]
        public void AddLootItem_ItemCanBeEnumerated()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player);
            instance.AddLootItem(100u, LootItemType.StaticItem, 3u);

            var items = instance.ToList();
            Assert.Single(items);
            Assert.Equal(100u, items[0].StaticId);
            Assert.Equal(LootItemType.StaticItem, items[0].Type);
            Assert.Equal(3u, items[0].Amount);
        }

        [Fact]
        public void HasLooter_ReturnsTrueForRegisteredLooter()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player);
            instance.AddLooter(12345uL, 99u);

            Assert.True(instance.HasLooter(12345uL));
            Assert.False(instance.HasLooter(99999uL));
        }

        [Fact]
        public void HasLootInstanceId_ReturnsTrueForExistingItem()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);

            var items = instance.ToList();
            Assert.True(instance.HasLootInstanceId(items[0].Id));
            Assert.False(instance.HasLootInstanceId(999999));
        }

        [Fact]
        public void HasExpired_FalseInitially()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);

            Assert.False(instance.HasExpired);
        }

        [Fact]
        public void HasExpired_TrueAfterTimerElapsed()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);

            // Simulate 1801 seconds passing (exceeds 1800s expiry)
            instance.Update(1801d);

            Assert.True(instance.HasExpired);
        }

        [Fact]
        public void HasExpired_TrueWhenAllItemsDelivered()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLooter(1uL, 1u);

            var player = CreateMockPlayer(1uL, 1u);
            var items = instance.ToList();
            items[0].DeliverItem(player);

            Assert.True(instance.HasExpired);
        }

        [Fact]
        public void MultipleItems_TrackedIndependently()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLootItem(200u, LootItemType.Cash, 500u);
            instance.AddLootItem(300u, LootItemType.AccountCurrency, 10u);

            var items = instance.ToList();
            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.False(i.Delivered));
        }

        private static IPlayer CreateMockPlayer(ulong characterId = 1, uint guid = 1)
        {
            var mockSession = new Mock<IGameSession>();
            var mockInventory = new Mock<IInventory>();
            var mockCurrency = new Mock<ICurrencyManager>();
            var mockQuest = new Mock<IQuestManager>();
            var mockAccount = new Mock<IAccount>();
            var mockAccountCurrency = new Mock<IAccountCurrencyManager>();

            mockAccount.Setup(a => a.CurrencyManager).Returns(mockAccountCurrency.Object);

            var mockPlayer = new Mock<IPlayer>();
            mockPlayer.Setup(p => p.CharacterId).Returns(characterId);
            mockPlayer.Setup(p => p.Guid).Returns(guid);
            mockPlayer.Setup(p => p.Session).Returns(mockSession.Object);
            mockPlayer.Setup(p => p.Inventory).Returns(mockInventory.Object);
            mockPlayer.Setup(p => p.CurrencyManager).Returns(mockCurrency.Object);
            mockPlayer.Setup(p => p.QuestManager).Returns(mockQuest.Object);
            mockPlayer.Setup(p => p.Account).Returns(mockAccount.Object);

            return mockPlayer.Object;
        }
    }
}
