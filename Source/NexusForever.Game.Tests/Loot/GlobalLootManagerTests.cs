using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Database;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Currency;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Loot;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Static;
using Moq;

namespace NexusForever.Game.Tests.Loot
{
    public class GlobalLootManagerTests
    {
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
        public void Update_PublishesPendingDropForRetrieval()
        {
            GlobalLootManager manager = CreateManager(CreateLootTableData(
                [CreateEntityMapping(100u, 1ul)],
                [CreateLootGroup(1ul, staticItemId: 1001u)]));
            var inventory = new Mock<IInventory>();
            IPlayer player = CreatePlayer(inventory);
            manager.Initialise();
            ILootInstance instance = manager.DropLoot(player, CreateEntity(100u));
            int lootItemId = instance.Single(item => item.Type == LootItemType.StaticItem).Id;

            manager.GiveLoot(player, lootItemId);
            inventory.Verify(i => i.ItemCreate(
                It.IsAny<InventoryLocation>(), It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<ItemUpdateReason>(), It.IsAny<uint>()), Times.Never);

            manager.Update(0d);
            manager.GiveLoot(player, lootItemId);

            inventory.Verify(i => i.ItemCreate(
                InventoryLocation.Inventory, 1001u, 1u, ItemUpdateReason.Loot, 0u), Times.Once);
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

        private static IPlayer CreatePlayer(Mock<IInventory> inventory = null)
        {
            var accountCurrencyManager = new Mock<IAccountCurrencyManager>();
            var account = new Mock<IAccount>();
            account.Setup(a => a.CurrencyManager).Returns(accountCurrencyManager.Object);

            var player = new Mock<IPlayer>();
            player.Setup(p => p.CharacterId).Returns(1ul);
            player.Setup(p => p.Guid).Returns(10u);
            player.Setup(p => p.Level).Returns(1u);
            player.Setup(p => p.Position).Returns(Vector3.Zero);
            player.Setup(p => p.Account).Returns(account.Object);
            player.Setup(p => p.Inventory).Returns((inventory ?? new Mock<IInventory>()).Object);
            player.Setup(p => p.Session).Returns(new Mock<IGameSession>().Object);
            return player.Object;
        }

        private static IWorldEntity CreateEntity(uint creatureId)
        {
            var entity = new Mock<IWorldEntity>();
            entity.Setup(e => e.CreatureId).Returns(creatureId);
            entity.Setup(e => e.Guid).Returns(20u);
            entity.Setup(e => e.Position).Returns(Vector3.Zero);
            return entity.Object;
        }
    }
}
