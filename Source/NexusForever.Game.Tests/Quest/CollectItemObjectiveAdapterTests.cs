using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Tests.Persistence;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared;
using GameItem = NexusForever.Game.Entity.Item;

namespace NexusForever.Game.Tests.Quest
{
    [Collection(InventoryPersistenceCollection.Name)]
    public sealed class CollectItemObjectiveAdapterTests : IDisposable
    {
        private const uint StackableItemId = 201u;
        private const uint NonStackableItemId = 202u;

        private readonly IServiceProvider originalServiceProvider;
        private readonly ImmutableDictionary<InventoryLocation, uint> originalCapacities;
        private readonly ServiceProvider serviceProvider;
        private readonly ItemManager itemManager = new();
        private readonly IItemInfo stackableItemInfo;
        private readonly IItemInfo nonStackableItemInfo;

        public CollectItemObjectiveAdapterTests()
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
        public void ItemCreate_MultipleNewStacksReportsOneAggregateGrant()
        {
            Inventory inventory = CreateInventory(3u, out _, out Mock<IQuestManager> questManager);

            inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 25u);

            Assert.Equal([10u, 10u, 5u], GetItems(inventory)
                .OrderBy(item => item.BagIndex)
                .Select(item => item.StackCount));
            VerifyGrant(questManager, StackableItemId, 25u);
        }

        [Fact]
        public void ItemCreate_ExistingHeadroomAndNewStackReportsOneCombinedGrant()
        {
            Inventory inventory = CreateInventory(2u, out _, out Mock<IQuestManager> questManager,
                CreateItemModel(600ul, StackableItemId, 0u, 8u));

            inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 5u);

            Assert.Equal([10u, 3u], GetItems(inventory)
                .OrderBy(item => item.BagIndex)
                .Select(item => item.StackCount));
            VerifyGrant(questManager, StackableItemId, 5u);
        }

        [Fact]
        public void ItemCreate_FullBagReportsOnlyActuallyGrantedCount()
        {
            Inventory inventory = CreateInventory(1u, out _, out Mock<IQuestManager> questManager);

            inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 15u);

            Assert.Equal(10u, Assert.Single(GetItems(inventory)).StackCount);
            VerifyGrant(questManager, StackableItemId, 10u);
        }

        [Fact]
        public void TryItemCreate_RejectedPreflightDoesNotReportGrant()
        {
            Inventory inventory = CreateInventory(1u, out _, out Mock<IQuestManager> questManager);

            bool created = inventory.TryItemCreate(
                InventoryLocation.Inventory,
                stackableItemInfo,
                11u,
                out uint remaining);

            Assert.False(created);
            Assert.Equal(1u, remaining);
            Assert.Empty(GetItems(inventory));
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void LoadMoveSplitAndDeleteDoNotReportAcquisition()
        {
            Inventory inventory = CreateInventory(3u, out _, out Mock<IQuestManager> questManager,
                CreateItemModel(601ul, StackableItemId, 0u, 6u));
            IItem loadedItem = Assert.Single(GetItems(inventory));

            inventory.ItemMove(loadedItem, InventoryLocation.Inventory, 1u);
            inventory.ItemSplit(loadedItem.Guid, new ItemLocation
            {
                Location = InventoryLocation.Inventory,
                BagIndex = 0u
            }, 2u);
            inventory.ItemDelete(StackableItemId, 1u);
            inventory.ItemDelete(new ItemLocation
            {
                Location = InventoryLocation.Inventory,
                BagIndex = 0u
            });

            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void ItemSplit_ZeroCountIsRejectedWithoutMutationOrPublication()
        {
            Inventory inventory = CreateInventory(
                2u,
                out Mock<IGameSession> session,
                out _,
                CreateItemModel(603ul, StackableItemId, 0u, 6u));
            IItem source = Assert.Single(GetItems(inventory));
            session.Invocations.Clear();

            Assert.Throws<InvalidPacketValueException>(() => inventory.ItemSplit(
                source.Guid,
                new ItemLocation
                {
                    Location = InventoryLocation.Inventory,
                    BagIndex = 1u
                },
                0u));

            Assert.Same(source, Assert.Single(GetItems(inventory)));
            Assert.Equal(6u, source.StackCount);
            Assert.Equal(0u, source.BagIndex);
            session.Verify(
                gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Never);
        }

        [Fact]
        public void ItemSplit_PositiveSubsetPreservesCountsAndPublication()
        {
            Inventory inventory = CreateInventory(
                2u,
                out Mock<IGameSession> session,
                out _,
                CreateItemModel(604ul, StackableItemId, 0u, 6u));
            IItem source = Assert.Single(GetItems(inventory));
            session.Invocations.Clear();

            inventory.ItemSplit(
                source.Guid,
                new ItemLocation
                {
                    Location = InventoryLocation.Inventory,
                    BagIndex = 1u
                },
                2u);

            IItem split = Assert.Single(GetItems(inventory), item => item.Guid != source.Guid);
            Assert.Equal(4u, source.StackCount);
            Assert.Equal(0u, source.BagIndex);
            Assert.Equal(2u, split.StackCount);
            Assert.Equal(1u, split.BagIndex);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerItemAdd>(message =>
                    message.InventoryItem.Item.Guid == split.Guid
                    && message.InventoryItem.Item.StackCount == 2u
                    && message.InventoryItem.Item.LocationData.Location == InventoryLocation.Inventory
                    && message.InventoryItem.Item.LocationData.BagIndex == 1u
                    && message.InventoryItem.Reason == ItemUpdateReason.NoReason)), Times.Once);
            session.Verify(gameSession => gameSession.EnqueueMessageEncrypted(
                It.Is<ServerItemStackCountUpdate>(message =>
                    message.Guid == source.Guid
                    && message.StackCount == 4u
                    && message.Reason == ItemUpdateReason.NoReason)), Times.Once);
            session.Verify(
                gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Exactly(2));
        }

        [Fact]
        public void ItemCreate_NewStackPacketFailureStillReportsCommittedGrantOnce()
        {
            Inventory inventory = CreateInventory(
                1u,
                out Mock<IGameSession> session,
                out Mock<IQuestManager> questManager);
            session
                .Setup(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<ServerItemAdd>()))
                .Throws<InvalidOperationException>();

            Assert.Throws<InvalidOperationException>(() =>
                inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 4u));

            Assert.Equal(4u, Assert.Single(GetItems(inventory)).StackCount);
            VerifyGrant(questManager, StackableItemId, 4u);
        }

        [Fact]
        public void ItemCreate_StackUpdatePacketFailureStillReportsCommittedGrantOnce()
        {
            Inventory inventory = CreateInventory(
                1u,
                out Mock<IGameSession> session,
                out Mock<IQuestManager> questManager,
                CreateItemModel(602ul, StackableItemId, 0u, 5u));
            session
                .Setup(gameSession => gameSession.EnqueueMessageEncrypted(It.IsAny<ServerItemStackCountUpdate>()))
                .Throws<InvalidOperationException>();

            Assert.Throws<InvalidOperationException>(() =>
                inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 2u));

            Assert.Equal(7u, Assert.Single(GetItems(inventory)).StackCount);
            VerifyGrant(questManager, StackableItemId, 2u);
        }

        [Fact]
        public void ItemCreate_QuestUpdateFailureDoesNotUndoOrFailGrant()
        {
            Inventory inventory = CreateInventory(1u, out _, out Mock<IQuestManager> questManager);
            questManager
                .Setup(manager => manager.ObjectiveUpdate(
                    QuestObjectiveType.CollectItem,
                    StackableItemId,
                    3u))
                .Throws<InvalidOperationException>();

            Exception exception = Record.Exception(() =>
                inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 3u));

            Assert.Null(exception);
            Assert.Equal(3u, Assert.Single(GetItems(inventory)).StackCount);
            VerifyGrant(questManager, StackableItemId, 3u);
        }

        [Fact]
        public void PublicAddItem_ReportsMailOrBuybackStyleGrantOnce()
        {
            Inventory inventory = CreateInventory(1u, out _, out Mock<IQuestManager> questManager);
            var item = new GameItem(42ul, stackableItemInfo, 4u);

            inventory.AddItem(item, InventoryLocation.Inventory, ItemUpdateReason.Buyback);

            Assert.Same(item, Assert.Single(GetItems(inventory)));
            VerifyGrant(questManager, StackableItemId, 4u);
        }

        [Fact]
        public void ZeroCountCreateAndPublicAddDoNotReportGrant()
        {
            Inventory inventory = CreateInventory(1u, out _, out Mock<IQuestManager> questManager);

            Assert.True(inventory.TryItemCreate(
                InventoryLocation.Inventory,
                stackableItemInfo,
                0u,
                out uint remaining));
            inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 0u);
            inventory.AddItem(new GameItem(42ul, stackableItemInfo, 0u), InventoryLocation.Inventory);

            Assert.Equal(0u, remaining);
            Assert.Equal(0u, Assert.Single(GetItems(inventory)).StackCount);
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void NonInventoryGrantDoesNotReportCollectItemObjective()
        {
            Inventory inventory = CreateInventory(1u, out _, out Mock<IQuestManager> questManager);

            inventory.ItemCreate(InventoryLocation.Ability, stackableItemInfo, 1u);
            inventory.AddItem(new GameItem(42ul, stackableItemInfo, 1u), InventoryLocation.Ability);

            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void MissingQuestManagerDoesNotFailInventoryGrant()
        {
            Inventory inventory = CreateInventory(1u, out _, out _, questManagerAvailable: false);

            Exception exception = Record.Exception(() =>
                inventory.ItemCreate(InventoryLocation.Inventory, stackableItemInfo, 1u));

            Assert.Null(exception);
            Assert.Equal(1u, Assert.Single(GetItems(inventory)).StackCount);
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = originalServiceProvider;
            SetInventoryCapacities(originalCapacities);
            serviceProvider.Dispose();
        }

        private Inventory CreateInventory(
            uint capacity,
            out Mock<IGameSession> session,
            out Mock<IQuestManager> questManager,
            params ItemModel[] items)
        {
            return CreateInventory(capacity, out session, out questManager, true, items);
        }

        private Inventory CreateInventory(
            uint capacity,
            out Mock<IGameSession> session,
            out Mock<IQuestManager> questManager,
            bool questManagerAvailable,
            params ItemModel[] items)
        {
            SetInventoryCapacities(ImmutableDictionary<InventoryLocation, uint>.Empty
                .Add(InventoryLocation.Inventory, capacity)
                .Add(InventoryLocation.Ability, 2u));

            var model = new CharacterModel { Id = 42ul };
            foreach (ItemModel item in items)
                model.Item.Add(item);

            session = new Mock<IGameSession>();
            questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(instance => instance.CharacterId).Returns(model.Id);
            player.SetupGet(instance => instance.Session).Returns(session.Object);
            player.SetupGet(instance => instance.IsLoading).Returns(false);
            player
                .SetupGet(instance => instance.QuestManager)
                .Returns(questManagerAvailable ? questManager.Object : null);
            return new Inventory(player.Object, model);
        }

        private static void VerifyGrant(Mock<IQuestManager> questManager, uint itemId, uint count)
        {
            questManager.Verify(
                manager => manager.ObjectiveUpdate(QuestObjectiveType.CollectItem, itemId, count),
                Times.Once);
            questManager.Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Once);
        }

        private static ItemModel CreateItemModel(ulong guid, uint itemId, uint bagIndex, uint stackCount)
        {
            return new ItemModel
            {
                Id = guid,
                OwnerId = 42ul,
                ItemId = itemId,
                Location = (ushort)InventoryLocation.Inventory,
                BagIndex = bagIndex,
                StackCount = stackCount,
                Durability = 1f
            };
        }

        private static List<IItem> GetItems(Inventory inventory)
        {
            return inventory.SelectMany(bag => bag).ToList();
        }

        private static IItemInfo CreateItemInfo(uint id, uint maximumStackCount, bool isStackable)
        {
            var info = new Mock<IItemInfo>();
            info.SetupGet(instance => instance.Id).Returns(id);
            info.SetupGet(instance => instance.Entry).Returns(new Item2Entry
            {
                Id = id,
                MaxStackCount = maximumStackCount
            });
            info.Setup(instance => instance.IsStackable()).Returns(isStackable);
            info.Setup(instance => instance.IsEquippable()).Returns(false);
            info.Setup(instance => instance.IsEquippableBag()).Returns(false);
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
    }
}
