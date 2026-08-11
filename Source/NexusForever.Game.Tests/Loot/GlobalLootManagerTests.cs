using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Database;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Currency;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Loot;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model.Loot;
using NexusForever.Network.World.Message.Static;
using Moq;

namespace NexusForever.Game.Tests.Loot
{
    public class GlobalLootManagerTests
    {
        private static readonly IBaseMap DefaultMap = Mock.Of<IBaseMap>();

        [Fact]
        public void Initialise_FlatThreeLevelHierarchy_ProducesGrandchildLoot()
        {
            LootTableData data = CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [
                    CreateLootGroup(1ul, staticItemId: 1001u),
                    CreateLootGroup(2ul, 1ul, 2001u),
                    CreateLootGroup(3ul, 2ul, 3001u)
                ]);
            GlobalLootManager manager = CreateManager(data);
            IPlayer player = CreatePlayer();
            IWorldEntity entity = CreateEntity(100u);

            manager.Initialise();
            ILootInstance instance = manager.DropLoot(player, entity);

            Assert.NotNull(instance);
            Assert.Contains(instance, item => item.StaticId == 1001u && item.Type == LootItemType.StaticItem);
            Assert.Contains(instance, item => item.StaticId == 2001u && item.Type == LootItemType.StaticItem);
            Assert.Contains(instance, item => item.StaticId == 3001u && item.Type == LootItemType.StaticItem);
        }

        [Fact]
        public void Initialise_MissingMappedGroup_FailureDoesNotPoisonRetry()
        {
            LootTableData currentData = CreateLootTableData([CreateEntityMapping(100u, 99ul)], []);
            var provider = new Mock<ILootTableProvider>();
            provider.Setup(p => p.LoadLootTables()).Returns(() => currentData);
            var manager = new GlobalLootManager(provider.Object);

            Assert.Throws<DatabaseDataException>(() => manager.Initialise());

            currentData = CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]);
            manager.Initialise();

            Assert.NotNull(manager.DropLoot(CreatePlayer(), CreateEntity(100u)));
            Assert.Throws<InvalidOperationException>(() => manager.Initialise());
        }

        [Fact]
        public void Initialise_OrphanedLootGroup_ThrowsDatabaseDataException()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [],
                [CreateLootGroup(2ul, parentId: 99ul)]));

            DatabaseDataException exception = Assert.Throws<DatabaseDataException>(() => manager.Initialise());

            Assert.Contains("missing parent 99", exception.Message);
        }

        [Fact]
        public void Initialise_CyclicLootHierarchy_ThrowsDatabaseDataException()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [],
                [CreateLootGroup(1ul, 2ul), CreateLootGroup(2ul, 1ul)]));

            DatabaseDataException exception = Assert.Throws<DatabaseDataException>(() => manager.Initialise());

            Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Initialise_UnsupportedLootItemType_ThrowsDatabaseDataException()
        {
            LootGroupModel group = CreateLootGroup(1ul);
            group.Item.Add(new LootItemModel
            {
                Id          = 10ul,
                Type        = uint.MaxValue,
                StaticId    = 1001u,
                Probability = 100f,
                MinCount    = 1u,
                MaxCount    = 1u,
                Comment     = ""
            });
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [group]));

            DatabaseDataException exception = Assert.Throws<DatabaseDataException>(() => manager.Initialise());

            Assert.Contains("unsupported type", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Update_BeforeInitialise_ThrowsInvalidOperationException()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData([], []));

            Assert.Throws<InvalidOperationException>(() => manager.Update(0.1d));
        }

        [Fact]
        public void Update_AdvancesActiveLootByActualTickDelta()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(CreatePlayer(), CreateEntity(100u));

            manager.Update(1801d);

            Assert.True(instance.HasExpired);
        }

        [Fact]
        public void DropLoot_WithoutCreatureTable_RollsAndAwardsOmnibits()
        {
            int chanceRolls = 0;
            int amountRolls = 0;
            var accountCurrencyManager = new Mock<IAccountCurrencyManager>();
            var session = new Mock<IGameSession>();
            GlobalLootManager manager = CreateManager(
                CreateLootTableData([], []),
                () =>
                {
                    chanceRolls++;
                    return 0d;
                },
                (minimum, maximum) =>
                {
                    amountRolls++;
                    Assert.Equal(0, minimum);
                    Assert.Equal(19, maximum);
                    return 0;
                });
            IPlayer player = CreatePlayer(
                level: 10u,
                accountCurrencyManager: accountCurrencyManager,
                session: session);
            Mock<IWorldEntity> entity = CreateEntityMock(100u);
            manager.Initialise();

            ILootInstance instance = manager.DropLoot(player, entity.Object);

            Assert.NotNull(instance);
            ILootInstanceItem omnibits = Assert.Single(instance);
            Assert.Equal(LootItemType.AccountCurrency, omnibits.Type);
            Assert.Equal((uint)AccountCurrencyType.Omnibits, omnibits.StaticId);
            Assert.Equal(17u, omnibits.Amount);
            Assert.True(omnibits.Delivered);
            Assert.Equal(1, chanceRolls);
            Assert.Equal(1, amountRolls);
            accountCurrencyManager.Verify(
                currency => currency.CurrencyAddAmount(AccountCurrencyType.Omnibits, 17ul, 0ul),
                Times.Once);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerLootNotify>(message =>
                    message.OwnerUnitId == entity.Object.Guid
                    && message.LootItems.Count > 0
                    && message.LootItems.All(item => item.Type == LootItemType.AccountCurrency))),
                Times.Once);
            entity.Verify(owner => owner.AddLoot(instance), Times.Once);
            entity.Verify(owner => owner.RemoveLoot(instance), Times.Once);
        }

        [Fact]
        public void DropLoot_WithEmptyCreatureTable_RollsAndAwardsOmnibits()
        {
            int chanceRolls = 0;
            var accountCurrencyManager = new Mock<IAccountCurrencyManager>();
            GlobalLootManager manager = CreateManager(
                CreateLootTableData(
                    [CreateEntityMapping(100u, 1ul)],
                    [CreateLootGroup(1ul)]),
                () =>
                {
                    chanceRolls++;
                    return 0d;
                },
                static (_, _) => 18);
            IPlayer player = CreatePlayer(
                level: 10u,
                accountCurrencyManager: accountCurrencyManager);
            manager.Initialise();

            ILootInstance instance = manager.DropLoot(player, CreateEntity(100u));

            Assert.NotNull(instance);
            ILootInstanceItem omnibits = Assert.Single(instance);
            Assert.Equal(35u, omnibits.Amount);
            Assert.True(omnibits.Delivered);
            Assert.Equal(1, chanceRolls);
            accountCurrencyManager.Verify(
                currency => currency.CurrencyAddAmount(AccountCurrencyType.Omnibits, 35ul, 0ul),
                Times.Once);
        }

        [Fact]
        public void DropLoot_WithNormalCreatureTable_RollsItemsAndOmnibitsOnce()
        {
            int chanceRolls = 0;
            int amountRolls = 0;
            var accountCurrencyManager = new Mock<IAccountCurrencyManager>();
            GlobalLootManager manager = CreateManager(
                CreateLootTableData(
                    [CreateEntityMapping(100u, 1ul)],
                    [CreateLootGroup(1ul, staticItemId: 1001u)]),
                () =>
                {
                    chanceRolls++;
                    return 0d;
                },
                (_, _) =>
                {
                    amountRolls++;
                    return 5;
                });
            IPlayer player = CreatePlayer(
                level: 10u,
                accountCurrencyManager: accountCurrencyManager);
            manager.Initialise();

            ILootInstance instance = manager.DropLoot(player, CreateEntity(100u));

            Assert.NotNull(instance);
            Assert.Contains(instance, item => item.Type == LootItemType.StaticItem && item.StaticId == 1001u && !item.Delivered);
            Assert.Contains(instance, item => item.Type == LootItemType.AccountCurrency && item.Amount == 22u && item.Delivered);
            Assert.Equal(1, chanceRolls);
            Assert.Equal(1, amountRolls);
            accountCurrencyManager.Verify(
                currency => currency.CurrencyAddAmount(AccountCurrencyType.Omnibits, 22ul, 0ul),
                Times.Once);
        }

        [Fact]
        public void DropLoot_IsImmediatelyAvailableForRetrieval()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            var inventory = new Mock<IInventory>();
            IPlayer player = CreatePlayer(inventory);
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(player, CreateEntity(100u));
            uint lootItemId = instance.Single(item => item.Type == LootItemType.StaticItem).Id;

            bool delivered = manager.GiveLoot(player, 20u, lootItemId);

            Assert.True(delivered);
            inventory.Verify(i => i.TryItemCreate(
                InventoryLocation.Inventory,
                1001u,
                1u,
                out It.Ref<uint>.IsAny,
                ItemUpdateReason.Loot,
                0u), Times.Once);
        }

        [Fact]
        public void GiveLoot_WithMismatchedOwner_DoesNotDeliver()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            var inventory = new Mock<IInventory>();
            IPlayer player = CreatePlayer(inventory);
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(player, CreateEntity(100u));
            uint lootItemId = instance.Single(item => item.Type == LootItemType.StaticItem).Id;

            bool delivered = manager.GiveLoot(player, 999u, lootItemId);

            Assert.False(delivered);
            inventory.Verify(i => i.TryItemCreate(
                It.IsAny<InventoryLocation>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                out It.Ref<uint>.IsAny,
                It.IsAny<ItemUpdateReason>(),
                It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void GiveLoot_FromDifferentMap_DoesNotDeliver()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            var inventory = new Mock<IInventory>();
            IPlayer authorisedPlayer = CreatePlayer(inventory, DefaultMap);
            IWorldEntity entity = CreateEntity(100u, DefaultMap);
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(authorisedPlayer, entity);
            uint lootItemId = instance.Single(item => item.Type == LootItemType.StaticItem).Id;
            IPlayer relocatedPlayer = CreatePlayer(inventory, Mock.Of<IBaseMap>());

            bool delivered = manager.GiveLoot(relocatedPlayer, entity.Guid, lootItemId);

            Assert.False(delivered);
            inventory.Verify(i => i.TryItemCreate(
                It.IsAny<InventoryLocation>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                out It.Ref<uint>.IsAny,
                It.IsAny<ItemUpdateReason>(),
                It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void GiveLoot_AfterOwnerLeavesWorld_RemovesStaleLoot()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            IPlayer player = CreatePlayer();
            var entity = new Mock<IWorldEntity>();
            IBaseMap entityMap = DefaultMap;
            entity.Setup(e => e.CreatureId).Returns(100u);
            entity.Setup(e => e.Guid).Returns(() => entityMap == null ? 0u : 20u);
            entity.Setup(e => e.Map).Returns(() => entityMap);
            entity.Setup(e => e.Position).Returns(Vector3.Zero);
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(player, entity.Object);
            uint lootItemId = instance.Single(item => item.Type == LootItemType.StaticItem).Id;
            entityMap = null;

            bool delivered = manager.GiveLoot(player, 20u, lootItemId);

            Assert.False(delivered);
            entity.Verify(e => e.RemoveLoot(instance), Times.Once);
        }

        [Fact]
        public void GiveLoot_FinalItem_DetachesLootFromOwner()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            IPlayer player = CreatePlayer();
            Mock<IWorldEntity> entity = CreateEntityMock(100u);
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(player, entity.Object);
            uint lootItemId = instance.Single(item => item.Type == LootItemType.StaticItem).Id;

            bool delivered = manager.GiveLoot(player, entity.Object.Guid, lootItemId);

            Assert.True(delivered);
            entity.Verify(e => e.AddLoot(instance), Times.Once);
            entity.Verify(e => e.RemoveLoot(instance), Times.Once);
        }

        [Fact]
        public void GiveLoot_InventoryInitiallyFull_CanRetryAfterCapacityIsAvailable()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            var inventory = new Mock<IInventory>();
            bool hasCapacity = false;
            inventory.Setup(i => i.TryItemCreate(
                    InventoryLocation.Inventory,
                    1001u,
                    1u,
                    out It.Ref<uint>.IsAny,
                    ItemUpdateReason.Loot,
                    0u))
                .Returns(() => hasCapacity);
            IPlayer player = CreatePlayer(inventory, configureInventory: false);
            Mock<IWorldEntity> entity = CreateEntityMock(100u);
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(player, entity.Object);
            uint lootItemId = instance.Single(item => item.Type == LootItemType.StaticItem).Id;

            Assert.False(manager.GiveLoot(player, entity.Object.Guid, lootItemId));

            hasCapacity = true;
            Assert.True(manager.GiveLoot(player, entity.Object.Guid, lootItemId));
            entity.Verify(e => e.RemoveLoot(instance), Times.Once);
        }

        [Fact]
        public void Update_ExpiredLoot_DetachesLootFromOwner()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            Mock<IWorldEntity> entity = CreateEntityMock(100u);
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(CreatePlayer(), entity.Object);

            manager.Update(1801d);

            entity.Verify(e => e.RemoveLoot(instance), Times.Once);
        }

        [Fact]
        public void ParallelDrops_AreAllAdvancedByWorldUpdate()
        {
            const int dropCount = 256;
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            IPlayer player = CreatePlayer();
            IWorldEntity entity = CreateEntity(100u);
            var instances = new ConcurrentBag<ILootInstance>();
            manager.Initialise();

            Parallel.For(0, dropCount, _ => instances.Add(manager.DropLoot(player, entity)));
            manager.Update(1801d);

            Assert.Equal(dropCount, instances.Count);
            Assert.All(instances, instance => Assert.True(instance.HasExpired));
        }

        [Fact]
        public void AddGameLoot_ResolvesProviderAndGlobalManager()
        {
            var services = new ServiceCollection();
            services.AddSingleton(new Mock<IDatabaseManager>().Object);
            services.AddGameLoot();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.IsType<WorldDatabaseLootTableProvider>(provider.GetRequiredService<ILootTableProvider>());
            Assert.IsType<GlobalLootManager>(provider.GetRequiredService<IGlobalLootManager>());
        }

        private static GlobalLootManager CreateManager(LootTableData data)
        {
            var provider = new Mock<ILootTableProvider>();
            provider.Setup(p => p.LoadLootTables()).Returns(data);
            return new GlobalLootManager(provider.Object);
        }

        private static GlobalLootManager CreateManager(
            LootTableData data,
            Func<double> omnibitChanceRoll,
            Func<int, int, int> omnibitAmountRoll)
        {
            var provider = new Mock<ILootTableProvider>();
            provider.Setup(p => p.LoadLootTables()).Returns(data);
            return new GlobalLootManager(provider.Object, omnibitChanceRoll, omnibitAmountRoll);
        }

        private static LootTableData CreateLootTableData(
            IEnumerable<EntityLootModel> entityLoot,
            IEnumerable<LootGroupModel> lootGroups)
        {
            return new LootTableData(entityLoot, [], lootGroups);
        }

        private static EntityLootModel CreateEntityMapping(uint creatureId, ulong lootGroupId)
        {
            return new EntityLootModel
            {
                Id          = creatureId,
                LootGroupId = lootGroupId,
                Comment     = ""
            };
        }

        private static LootGroupModel CreateLootGroup(
            ulong id,
            ulong? parentId = null,
            uint? staticItemId = null)
        {
            var group = new LootGroupModel
            {
                Id            = id,
                ParentId      = parentId,
                Probability   = 100f,
                MinDrop       = 0u,
                MaxDrop       = 0u,
                ConditionType = 0u,
                Condition     = 0u,
                Comment       = ""
            };

            if (staticItemId.HasValue)
            {
                group.Item.Add(new LootItemModel
                {
                    Id          = id,
                    Type        = (uint)LootItemType.StaticItem,
                    StaticId    = staticItemId.Value,
                    Probability = 100f,
                    MinCount    = 1u,
                    MaxCount    = 1u,
                    Comment     = ""
                });
            }

            return group;
        }

        private static IPlayer CreatePlayer(
            Mock<IInventory> inventory = null,
            IBaseMap map = null,
            bool configureInventory = true,
            uint level = 1u,
            Mock<IAccountCurrencyManager> accountCurrencyManager = null,
            Mock<IGameSession> session = null)
        {
            accountCurrencyManager ??= new Mock<IAccountCurrencyManager>();
            var account = new Mock<IAccount>();
            account.Setup(a => a.CurrencyManager).Returns(accountCurrencyManager.Object);

            inventory ??= new Mock<IInventory>();
            if (configureInventory)
            {
                inventory.Setup(i => i.TryItemCreate(
                        It.IsAny<InventoryLocation>(),
                        It.IsAny<uint>(),
                        It.IsAny<uint>(),
                        out It.Ref<uint>.IsAny,
                        It.IsAny<ItemUpdateReason>(),
                        It.IsAny<uint>()))
                    .Returns(true);
            }

            var player = new Mock<IPlayer>();
            player.Setup(p => p.CharacterId).Returns(1ul);
            player.Setup(p => p.Guid).Returns(10u);
            player.Setup(p => p.Level).Returns(level);
            player.Setup(p => p.Position).Returns(Vector3.Zero);
            player.Setup(p => p.Map).Returns(map ?? DefaultMap);
            player.Setup(p => p.Account).Returns(account.Object);
            player.Setup(p => p.Inventory).Returns(inventory.Object);
            player.Setup(p => p.Session).Returns((session ?? new Mock<IGameSession>()).Object);
            return player.Object;
        }

        private static IWorldEntity CreateEntity(uint creatureId, IBaseMap map = null)
        {
            return CreateEntityMock(creatureId, map).Object;
        }

        private static Mock<IWorldEntity> CreateEntityMock(uint creatureId, IBaseMap map = null)
        {
            var entity = new Mock<IWorldEntity>();
            entity.Setup(e => e.CreatureId).Returns(creatureId);
            entity.Setup(e => e.Guid).Returns(20u);
            entity.Setup(e => e.Position).Returns(Vector3.Zero);
            entity.Setup(e => e.Map).Returns(map ?? DefaultMap);
            return entity;
        }
    }
}
