using System.Collections.Immutable;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Persistence
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class InventoryPersistenceCollection
    {
        public const string Name = "Inventory persistence";
    }

    [Collection(InventoryPersistenceCollection.Name)]
    public sealed class InventoryPersistenceTests : IDisposable
    {
        private const uint StackableItemId = 101u;
        private const uint NonStackableItemId = 102u;

        private readonly IServiceProvider originalServiceProvider;
        private readonly ImmutableDictionary<InventoryLocation, uint> originalCapacities;
        private readonly ServiceProvider serviceProvider;
        private readonly ItemManager itemManager = new();
        private readonly IItemInfo stackableItemInfo;
        private readonly IItemInfo nonStackableItemInfo;

        public InventoryPersistenceTests()
        {
            originalServiceProvider = LegacyServiceProvider.Provider;
            originalCapacities = AssetManager.InventoryLocationCapacities;

            stackableItemInfo = CreateItemInfo(StackableItemId, 10u, true);
            nonStackableItemInfo = CreateItemInfo(NonStackableItemId, 1u, false);
            SetItemManagerItems(stackableItemInfo, nonStackableItemInfo);

            serviceProvider = new ServiceCollection()
                .AddSingleton(itemManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        [Fact]
        public void TryItemCreate_InsufficientStackCapacityDoesNotMutateInventory()
        {
            Inventory inventory = CreateInventory(1u);

            bool created = inventory.TryItemCreate(
                InventoryLocation.Inventory,
                StackableItemId,
                11u,
                out uint remaining,
                ItemUpdateReason.Loot);

            Assert.False(created);
            Assert.Equal(1u, remaining);
            Assert.Empty(GetItems(inventory));
        }

        [Fact]
        public void TryItemCreate_UsesExistingStackHeadroomAndEmptySlotsAtomically()
        {
            Inventory inventory = CreateInventory(2u);
            inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 8u);

            bool rejected = inventory.TryItemCreate(
                InventoryLocation.Inventory,
                stackableItemInfo,
                13u,
                out uint rejectedRemaining,
                ItemUpdateReason.Loot);

            Assert.False(rejected);
            Assert.Equal(1u, rejectedRemaining);
            Assert.Equal([8u], GetItems(inventory).Select(item => item.StackCount));

            bool created = inventory.TryItemCreate(
                InventoryLocation.Inventory,
                stackableItemInfo,
                12u,
                out uint remaining,
                ItemUpdateReason.Loot);

            Assert.True(created);
            Assert.Equal(0u, remaining);
            Assert.Equal([10u, 10u], GetItems(inventory)
                .OrderBy(item => item.BagIndex)
                .Select(item => item.StackCount));
        }

        [Fact]
        public void TryItemCreate_NonStackableItemsRequireOneSlotEach()
        {
            Inventory inventory = CreateInventory(2u);

            Assert.False(inventory.TryItemCreate(
                InventoryLocation.Inventory,
                nonStackableItemInfo,
                3u,
                out uint rejectedRemaining));
            Assert.Equal(1u, rejectedRemaining);
            Assert.Empty(GetItems(inventory));

            Assert.True(inventory.TryItemCreate(
                InventoryLocation.Inventory,
                nonStackableItemInfo,
                2u,
                out uint remaining));
            Assert.Equal(0u, remaining);
            Assert.Equal(2, GetItems(inventory).Count);
        }

        [Fact]
        public void TryItemExchange_CapacityDriftReturnsFalseWithoutMutation()
        {
            Inventory inventory = CreateInventory(1u, out Mock<IGameSession> session);
            inventory.ItemCreate(InventoryLocation.Inventory, nonStackableItemInfo, 1u);
            session.Invocations.Clear();

            bool exchanged = inventory.TryItemExchange(
                [],
                [new KeyValuePair<IItemInfo, uint>(stackableItemInfo, 1u)],
                ItemUpdateReason.Quest);

            Assert.False(exchanged);
            Assert.Equal(NonStackableItemId, Assert.Single(GetItems(inventory)).Id);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()), Times.Never);
        }

        [Fact]
        public void TryAdmitItemExchange_PreflightRejectionReturnsFalseWithoutMutation()
        {
            Inventory inventory = CreateInventory(1u, out Mock<IGameSession> session);
            inventory.ItemCreate(InventoryLocation.Inventory, nonStackableItemInfo, 1u);
            session.Invocations.Clear();

            bool admitted = inventory.TryAdmitItemExchange(
                [],
                [new KeyValuePair<IItemInfo, uint>(stackableItemInfo, 1u)],
                ItemUpdateReason.Quest);

            Assert.False(admitted);
            Assert.Equal(NonStackableItemId, Assert.Single(GetItems(inventory)).Id);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()), Times.Never);
        }

        [Fact]
        public void TryAdmitItemExchange_PostMutationNotificationFailureReturnsTrueWithoutRetryingMutation()
        {
            Inventory inventory = CreateInventory(1u, out Mock<IGameSession> session);
            session
                .Setup(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<ServerItemAdd>()))
                .Throws<InvalidOperationException>();

            bool admitted = inventory.TryAdmitItemExchange(
                [],
                [new KeyValuePair<IItemInfo, uint>(nonStackableItemInfo, 1u)],
                ItemUpdateReason.Quest);

            Assert.True(admitted);
            Assert.Equal(NonStackableItemId, Assert.Single(GetItems(inventory)).Id);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<ServerItemAdd>()), Times.Once);
        }

        [Fact]
        public void TryItemExchange_FullBagReclamationMakesRewardFitWithQuestReason()
        {
            Inventory inventory = CreateInventory(1u, out Mock<IGameSession> session, new ItemModel
            {
                Id         = 600ul,
                OwnerId    = 42ul,
                ItemId     = StackableItemId,
                Location   = (ushort)InventoryLocation.Inventory,
                BagIndex   = 0u,
                StackCount = 1u,
                Durability = 1f
            });

            bool exchanged = inventory.TryItemExchange(
                [new KeyValuePair<uint, uint>(StackableItemId, 1u)],
                [new KeyValuePair<IItemInfo, uint>(nonStackableItemInfo, 1u)],
                ItemUpdateReason.Quest);

            Assert.True(exchanged);
            Assert.Equal(NonStackableItemId, Assert.Single(GetItems(inventory)).Id);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerItemDelete>(message => message.Reason == ItemUpdateReason.Quest)), Times.Once);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerItemAdd>(message => message.InventoryItem.Reason == ItemUpdateReason.Quest)), Times.Once);
        }

        [Fact]
        public void TryItemExchange_AggregatesDuplicateRemovalsAndAdditionsAcrossStacks()
        {
            Inventory inventory = CreateInventory(2u, out Mock<IGameSession> session,
                new ItemModel
                {
                    Id         = 601ul,
                    OwnerId    = 42ul,
                    ItemId     = StackableItemId,
                    Location   = (ushort)InventoryLocation.Inventory,
                    BagIndex   = 0u,
                    StackCount = 4u,
                    Durability = 1f
                },
                new ItemModel
                {
                    Id         = 602ul,
                    OwnerId    = 42ul,
                    ItemId     = StackableItemId,
                    Location   = (ushort)InventoryLocation.Inventory,
                    BagIndex   = 1u,
                    StackCount = 4u,
                    Durability = 1f
                });

            bool exchanged = inventory.TryItemExchange(
                [
                    new KeyValuePair<uint, uint>(StackableItemId, 3u),
                    new KeyValuePair<uint, uint>(StackableItemId, 5u)
                ],
                [
                    new KeyValuePair<IItemInfo, uint>(nonStackableItemInfo, 1u),
                    new KeyValuePair<IItemInfo, uint>(nonStackableItemInfo, 1u)
                ],
                ItemUpdateReason.Quest);

            Assert.True(exchanged);
            Assert.Equal([NonStackableItemId, NonStackableItemId], GetItems(inventory)
                .OrderBy(item => item.BagIndex)
                .Select(item => item.Id));
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerItemDelete>(message => message.Reason == ItemUpdateReason.Quest)), Times.Exactly(2));
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerItemAdd>(message => message.InventoryItem.Reason == ItemUpdateReason.Quest)), Times.Exactly(2));
        }

        [Fact]
        public void TryItemExchange_PartialStackRemovalUsesQuestReason()
        {
            Inventory inventory = CreateInventory(1u, out Mock<IGameSession> session, new ItemModel
            {
                Id         = 603ul,
                OwnerId    = 42ul,
                ItemId     = StackableItemId,
                Location   = (ushort)InventoryLocation.Inventory,
                BagIndex   = 0u,
                StackCount = 5u,
                Durability = 1f
            });

            bool exchanged = inventory.TryItemExchange(
                [
                    new KeyValuePair<uint, uint>(StackableItemId, 1u),
                    new KeyValuePair<uint, uint>(StackableItemId, 1u)
                ],
                [],
                ItemUpdateReason.Quest);

            Assert.True(exchanged);
            Assert.Equal(3u, Assert.Single(GetItems(inventory)).StackCount);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerItemStackCountUpdate>(message => message.Reason == ItemUpdateReason.Quest)), Times.Once);
        }

        [Fact]
        public void TryItemExchange_MissingRemovalQuantityDoesNotCreatePhantomCapacity()
        {
            Inventory inventory = CreateInventory(2u, out Mock<IGameSession> session,
                new ItemModel
                {
                    Id         = 604ul,
                    OwnerId    = 42ul,
                    ItemId     = StackableItemId,
                    Location   = (ushort)InventoryLocation.Inventory,
                    BagIndex   = 0u,
                    StackCount = 1u,
                    Durability = 1f
                },
                new ItemModel
                {
                    Id         = 605ul,
                    OwnerId    = 42ul,
                    ItemId     = NonStackableItemId,
                    Location   = (ushort)InventoryLocation.Inventory,
                    BagIndex   = 1u,
                    StackCount = 1u,
                    Durability = 1f
                });

            bool exchanged = inventory.TryItemExchange(
                [new KeyValuePair<uint, uint>(StackableItemId, 2u)],
                [new KeyValuePair<IItemInfo, uint>(nonStackableItemInfo, 2u)],
                ItemUpdateReason.Quest);

            Assert.False(exchanged);
            Assert.Equal([StackableItemId, NonStackableItemId], GetItems(inventory)
                .OrderBy(item => item.BagIndex)
                .Select(item => item.Id));
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()), Times.Never);
        }

        [Fact]
        public void Save_DeletedItemRemainsRetryableUntilCommitAcknowledgement()
        {
            Inventory inventory = CreateInventory(1u, new ItemModel
            {
                Id         = 500ul,
                OwnerId    = 42ul,
                ItemId     = StackableItemId,
                Location   = (ushort)InventoryLocation.Inventory,
                BagIndex   = 0u,
                StackCount = 1u,
                Durability = 1f
            });
            inventory.ItemDelete(new ItemLocation
            {
                Location = InventoryLocation.Inventory,
                BagIndex = 0u
            });

            using TestCharacterContext failedContext = CreateContext();
            var failedScope = new SaveCommitScope();
            inventory.Save(failedContext, failedScope);
            Assert.Equal(EntityState.Deleted, failedContext.ChangeTracker.Entries<ItemModel>().Single().State);

            using TestCharacterContext retryContext = CreateContext();
            var retryScope = new SaveCommitScope();
            inventory.Save(retryContext, retryScope);
            Assert.Equal(EntityState.Deleted, retryContext.ChangeTracker.Entries<ItemModel>().Single().State);

            retryScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext acknowledgedContext = CreateContext();
            inventory.Save(acknowledgedContext, new SaveCommitScope());
            Assert.Empty(acknowledgedContext.ChangeTracker.Entries<ItemModel>());
        }

        [Fact]
        public void Save_ItemMutationDuringPendingCommitRemainsDirtyAfterAcknowledgement()
        {
            Inventory inventory = CreateInventory(1u, new ItemModel
            {
                Id         = 501ul,
                OwnerId    = 42ul,
                ItemId     = StackableItemId,
                Location   = (ushort)InventoryLocation.Inventory,
                BagIndex   = 0u,
                StackCount = 1u,
                Durability = 1f
            });
            IItem item = GetItems(inventory).Single();
            item.StackCount = 2u;

            using TestCharacterContext firstContext = CreateContext();
            var firstScope = new SaveCommitScope();
            inventory.Save(firstContext, firstScope);

            item.StackCount = 3u;
            firstScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext secondContext = CreateContext();
            var secondScope = new SaveCommitScope();
            inventory.Save(secondContext, secondScope);

            ItemModel stagedItem = secondContext.ChangeTracker.Entries<ItemModel>().Single().Entity;
            Assert.Equal(3u, stagedItem.StackCount);
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = originalServiceProvider;
            SetInventoryCapacities(originalCapacities);
            serviceProvider.Dispose();
        }

        private Inventory CreateInventory(uint capacity, params ItemModel[] items)
        {
            return CreateInventory(capacity, out _, items);
        }

        private Inventory CreateInventory(uint capacity, out Mock<IGameSession> session, params ItemModel[] items)
        {
            SetInventoryCapacities(ImmutableDictionary<InventoryLocation, uint>.Empty
                .Add(InventoryLocation.Inventory, capacity));

            var model = new CharacterModel { Id = 42ul };
            foreach (ItemModel item in items)
                model.Item.Add(item);

            session = new Mock<IGameSession>();
            var player = new Mock<IPlayer>();
            player.SetupGet(p => p.CharacterId).Returns(model.Id);
            player.SetupGet(p => p.Session).Returns(session.Object);
            player.SetupGet(p => p.IsLoading).Returns(false);
            return new Inventory(player.Object, model);
        }

        private static List<IItem> GetItems(Inventory inventory)
        {
            return inventory.SelectMany(bag => bag).ToList();
        }

        private static IItemInfo CreateItemInfo(uint id, uint maximumStackCount, bool isStackable)
        {
            var info = new Mock<IItemInfo>();
            info.SetupGet(i => i.Id).Returns(id);
            info.SetupGet(i => i.Entry).Returns(new Item2Entry
            {
                Id            = id,
                MaxStackCount = maximumStackCount
            });
            info.Setup(i => i.IsStackable()).Returns(isStackable);
            info.Setup(i => i.IsEquippable()).Returns(false);
            info.Setup(i => i.IsEquippableBag()).Returns(false);
            return info.Object;
        }

        private void SetItemManagerItems(params IItemInfo[] items)
        {
            FieldInfo field = typeof(ItemManager).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(itemManager, items.ToImmutableDictionary(item => item.Id));
        }

        private static void SetInventoryCapacities(ImmutableDictionary<InventoryLocation, uint> capacities)
        {
            PropertyInfo property = typeof(AssetManager).GetProperty(
                nameof(AssetManager.InventoryLocationCapacities),
                BindingFlags.Static | BindingFlags.Public);
            property.SetValue(null, capacities);
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
    }
}
