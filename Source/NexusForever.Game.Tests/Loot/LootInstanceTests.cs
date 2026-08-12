using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Currency;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Loot;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model.Loot;
using NexusForever.Network.World.Message.Static;
using Moq;

namespace NexusForever.Game.Tests.Loot
{
    public class LootInstanceTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            var instance = new LootInstance(42u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
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
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
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
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLooter(12345uL, 99u);

            Assert.True(instance.HasLooter(12345uL));
            Assert.False(instance.HasLooter(99999uL));
        }

        [Fact]
        public void HasLootInstanceId_ReturnsTrueForExistingItem()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);

            var items = instance.ToList();
            Assert.True(instance.HasLootInstanceId(items[0].Id));
            Assert.False(instance.HasLootInstanceId(999999));
        }

        [Fact]
        public void HasExpired_FalseInitially()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);

            Assert.False(instance.HasExpired);
        }

        [Fact]
        public void HasExpired_TrueAfterTimerElapsed()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);

            // Simulate 1801 seconds passing (exceeds 1800s expiry)
            instance.Update(1801d);

            Assert.True(instance.HasExpired);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(0d)]
        [InlineData(-1d)]
        public void Update_InvalidTickDoesNotAdvanceOrPoisonExpiry(double lastTick)
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);

            instance.Update(lastTick);
            instance.Update(1799d);

            Assert.False(instance.HasExpired);

            instance.Update(1d);

            Assert.True(instance.HasExpired);
        }

        [Fact]
        public void AddLootItem_ZeroCountIsRejected()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                instance.AddLootItem(100u, LootItemType.StaticItem, 0u));
        }

        [Fact]
        public void HasExpired_TrueWhenAllItemsDelivered()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLooter(1uL, 1u);

            var player = CreateMockPlayer(1uL, 1u);
            var items = instance.ToList();
            items[0].SetWinner(player.CharacterId, player.Guid);
            items[0].DeliverItem(player);

            Assert.True(instance.HasExpired);
        }

        [Fact]
        public void MultipleItems_TrackedIndependently()
        {
            var instance = new LootInstance(1u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLootItem(200u, LootItemType.Cash, 500u);
            instance.AddLootItem(300u, LootItemType.AccountCurrency, 10u);

            var items = instance.ToList();
            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.False(i.Delivered));
        }

        [Fact]
        public void SendLootNotify_CreatureLoot_UsesOwnerAndClaimableItemIdentifiers()
        {
            var instance = new LootInstance(42u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLooter(1ul, 1u);
            (IPlayer player, Mock<IGameSession> session, _) = CreateMockPlayerState();
            uint lootItemId = instance.Single().Id;

            instance.SendLootNotify(player);

            session.Verify(s => s.EnqueueMessageEncrypted(It.Is<ServerLootNotify>(message =>
                message.OwnerUnitId == 42u
                && !message.Explosion
                && message.LootItems.Count == 1
                && message.LootItems[0].LootUnitId == lootItemId
                && message.LootItems[0].CanLoot
                && !message.LootItems[0].Explosion)), Times.Once);
        }

        [Fact]
        public void SendLootNotify_UnauthorisedPlayer_DoesNotSendPacket()
        {
            var instance = new LootInstance(42u, LootEntityType.Creature, LooterType.Player, System.Numerics.Vector3.Zero);
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLooter(2ul, 2u);
            (IPlayer player, Mock<IGameSession> session, _) = CreateMockPlayerState();

            instance.SendLootNotify(player);

            session.Verify(s => s.EnqueueMessageEncrypted(It.IsAny<IWritable>()), Times.Never);
            Assert.False(instance.Single().Delivered);
        }

        [Fact]
        public void SendLootNotify_LootBagWithCapacity_GrantsExplodingItemImmediately()
        {
            var instance = new LootInstance(1u, LootEntityType.Item, LooterType.Player, System.Numerics.Vector3.Zero)
            {
                Explosion = true
            };
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLooter(1ul, 1u);
            (IPlayer player, Mock<IGameSession> session, Mock<IInventory> inventory) = CreateMockPlayerState();

            instance.SendLootNotify(player);

            Assert.True(instance.HasExpired);
            inventory.Verify(i => i.TryItemCreate(
                InventoryLocation.Inventory,
                100u,
                1u,
                out It.Ref<uint>.IsAny,
                ItemUpdateReason.Loot,
                0u), Times.Once);
            session.Verify(s => s.EnqueueMessageEncrypted(It.Is<ServerLootNotify>(message =>
                message.OwnerUnitId == 1u
                && message.Explosion
                && message.LootItems.Count == 1
                && !message.LootItems[0].CanLoot
                && message.LootItems[0].Explosion)), Times.Once);
        }

        [Fact]
        public void SendLootNotify_LootBagWithoutCapacity_RemainsClaimable()
        {
            var instance = new LootInstance(1u, LootEntityType.Item, LooterType.Player, System.Numerics.Vector3.Zero)
            {
                Explosion = true
            };
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLooter(1ul, 1u);
            (IPlayer player, Mock<IGameSession> session, _) = CreateMockPlayerState(inventoryHasCapacity: false);

            instance.SendLootNotify(player);

            Assert.False(instance.HasExpired);
            session.Verify(s => s.EnqueueMessageEncrypted(It.Is<ServerLootNotify>(message =>
                message.OwnerUnitId == 1u
                && message.Explosion
                && message.LootItems.Count == 1
                && message.LootItems[0].CanLoot
                && message.LootItems[0].Explosion)), Times.Once);
        }

        [Fact]
        public void SendLootNotify_EagerDeliveryFailureStillAttemptsLaterItemWithoutReplay()
        {
            var instance = new LootInstance(1u, LootEntityType.Item, LooterType.Player, System.Numerics.Vector3.Zero)
            {
                Explosion = true
            };
            instance.AddLootItem(100u, LootItemType.StaticItem, 1u);
            instance.AddLootItem(200u, LootItemType.StaticItem, 1u);
            instance.AddLooter(1ul, 1u);
            (IPlayer player, Mock<IGameSession> session, Mock<IInventory> inventory) = CreateMockPlayerState();
            var attempts = new List<uint>();
            LootInstanceItem[] items = instance.Cast<LootInstanceItem>().ToArray();
            inventory.Setup(i => i.TryItemCreate(
                    InventoryLocation.Inventory,
                    100u,
                    1u,
                    out It.Ref<uint>.IsAny,
                    ItemUpdateReason.Loot,
                    0u))
                .Callback(() => attempts.Add(100u))
                .Throws(new InvalidOperationException("first reward failed"));
            inventory.Setup(i => i.TryItemCreate(
                    InventoryLocation.Inventory,
                    200u,
                    1u,
                    out It.Ref<uint>.IsAny,
                    ItemUpdateReason.Loot,
                    0u))
                .Callback(() => attempts.Add(200u))
                .Returns(true);

            Exception exception = Record.Exception(() => instance.SendLootNotify(player));

            Assert.Null(exception);
            Assert.Equal(new uint[] { 100u, 200u }, attempts);
            Assert.True(instance.HasExpired);
            Assert.All(items, item => Assert.True(item.Delivered));
            session.Verify(s => s.EnqueueMessageEncrypted(It.Is<ServerLootNotify>(message =>
                message.OwnerUnitId == 1u
                && message.Explosion
                && message.LootItems.Count == 2
                && message.LootItems[0].LootUnitId == items[0].Id
                && !message.LootItems[0].CanLoot
                && message.LootItems[0].Explosion
                && message.LootItems[1].LootUnitId == items[1].Id
                && !message.LootItems[1].CanLoot
                && message.LootItems[1].Explosion)), Times.Once);

            instance.SendLootNotify(player);

            Assert.Equal(new uint[] { 100u, 200u }, attempts);
            inventory.Verify(i => i.TryItemCreate(
                InventoryLocation.Inventory,
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                out It.Ref<uint>.IsAny,
                ItemUpdateReason.Loot,
                0u), Times.Exactly(2));
        }

        private static IPlayer CreateMockPlayer(ulong characterId = 1, uint guid = 1)
        {
            return CreateMockPlayerState(characterId: characterId, guid: guid).Player;
        }

        private static (IPlayer Player, Mock<IGameSession> Session, Mock<IInventory> Inventory) CreateMockPlayerState(
            bool inventoryHasCapacity = true,
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
                .Returns(inventoryHasCapacity);

            var mockPlayer = new Mock<IPlayer>();
            mockPlayer.Setup(p => p.CharacterId).Returns(characterId);
            mockPlayer.Setup(p => p.Guid).Returns(guid);
            mockPlayer.Setup(p => p.Session).Returns(mockSession.Object);
            mockPlayer.Setup(p => p.Inventory).Returns(mockInventory.Object);
            mockPlayer.Setup(p => p.CurrencyManager).Returns(mockCurrency.Object);
            mockPlayer.Setup(p => p.QuestManager).Returns(mockQuest.Object);
            mockPlayer.Setup(p => p.Account).Returns(mockAccount.Object);

            return (mockPlayer.Object, mockSession, mockInventory);
        }
    }
}
