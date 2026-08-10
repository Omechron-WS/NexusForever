using System.Reflection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Loot;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Loot;
using Moq;

namespace NexusForever.WorldServer.Tests
{
    public class LootHandlerTests
    {
        [Fact]
        public void ClientLootItem_Collect_ForwardsOwnerAndLootIdentifiers()
        {
            var lootManager = new Mock<IGlobalLootManager>();
            var player = new Mock<IPlayer>();
            IWorldSession session = CreateSession(player.Object);
            ClientLootItem message = CreateLootItemMessage(42u, 84u, false);
            var handler = new ClientLootItemHandler(lootManager.Object);

            handler.HandleMessage(session, message);

            lootManager.Verify(m => m.GiveLoot(player.Object, 42u, 84u), Times.Once);
        }

        [Fact]
        public void ClientLootItem_Request_DoesNotCollectSoloLoot()
        {
            var lootManager = new Mock<IGlobalLootManager>();
            var player = new Mock<IPlayer>();
            IWorldSession session = CreateSession(player.Object);
            ClientLootItem message = CreateLootItemMessage(42u, 84u, true);
            var handler = new ClientLootItemHandler(lootManager.Object);

            handler.HandleMessage(session, message);

            lootManager.Verify(m => m.GiveLoot(
                It.IsAny<IPlayer>(), It.IsAny<uint>(), It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void ClientItemUseLootBag_MismatchedGuid_DoesNotConsumeItem()
        {
            var lootManager = new Mock<IGlobalLootManager>();
            var inventory = new Mock<IInventory>();
            var item = CreateLootBag(55ul);
            inventory.Setup(i => i.GetItem(It.IsAny<NexusForever.Network.World.Message.Model.Shared.ItemLocation>()))
                .Returns(item.Object);
            var player = new Mock<IPlayer>();
            player.Setup(p => p.Inventory).Returns(inventory.Object);
            IWorldSession session = CreateSession(player.Object);
            ClientItemUseLootBag message = CreateLootBagMessage(99ul);
            var handler = new ClientItemUseLootBagHandler(lootManager.Object);

            handler.HandleMessage(session, message);

            inventory.Verify(i => i.ItemUse(It.IsAny<IItem>()), Times.Never);
            lootManager.Verify(m => m.DropLoot(It.IsAny<IPlayer>(), It.IsAny<IItem>()), Times.Never);
        }

        [Fact]
        public void ClientItemUseLootBag_UnconfiguredItem_DoesNotConsumeItem()
        {
            var lootManager = new Mock<IGlobalLootManager>();
            var inventory = new Mock<IInventory>();
            var item = CreateLootBag(55ul);
            inventory.Setup(i => i.GetItem(It.IsAny<NexusForever.Network.World.Message.Model.Shared.ItemLocation>()))
                .Returns(item.Object);
            var player = new Mock<IPlayer>();
            player.Setup(p => p.Inventory).Returns(inventory.Object);
            IWorldSession session = CreateSession(player.Object);
            ClientItemUseLootBag message = CreateLootBagMessage(55ul);
            var handler = new ClientItemUseLootBagHandler(lootManager.Object);

            handler.HandleMessage(session, message);

            lootManager.Verify(m => m.HasLootTable(item.Object), Times.Once);
            inventory.Verify(i => i.ItemUse(It.IsAny<IItem>()), Times.Never);
        }

        [Fact]
        public void ClientItemUseLootBag_ConfiguredItem_ConsumesThenDropsLoot()
        {
            var lootManager = new Mock<IGlobalLootManager>();
            var inventory = new Mock<IInventory>();
            var item = CreateLootBag(55ul);
            inventory.Setup(i => i.GetItem(It.IsAny<NexusForever.Network.World.Message.Model.Shared.ItemLocation>()))
                .Returns(item.Object);
            inventory.Setup(i => i.ItemUse(item.Object)).Returns(true);
            lootManager.Setup(m => m.HasLootTable(item.Object)).Returns(true);
            var player = new Mock<IPlayer>();
            player.Setup(p => p.Inventory).Returns(inventory.Object);
            IWorldSession session = CreateSession(player.Object);
            ClientItemUseLootBag message = CreateLootBagMessage(55ul);
            var handler = new ClientItemUseLootBagHandler(lootManager.Object);

            handler.HandleMessage(session, message);

            inventory.Verify(i => i.ItemUse(item.Object), Times.Once);
            lootManager.Verify(m => m.DropLoot(player.Object, item.Object), Times.Once);
        }

        private static IWorldSession CreateSession(IPlayer player)
        {
            var session = new Mock<IWorldSession>();
            session.Setup(s => s.Player).Returns(player);
            return session.Object;
        }

        private static Mock<IItem> CreateLootBag(ulong guid)
        {
            var info = new Mock<IItemInfo>();
            info.Setup(i => i.Entry).Returns(new Item2Entry
            {
                Id              = 100u,
                Item2CategoryId = 138u
            });

            var item = new Mock<IItem>();
            item.Setup(i => i.Guid).Returns(guid);
            item.Setup(i => i.Info).Returns(info.Object);
            return item;
        }

        private static ClientLootItem CreateLootItemMessage(uint ownerUnitId, uint lootUnitId, bool request)
        {
            var message = new ClientLootItem();
            SetProperty(message, nameof(ClientLootItem.OwnerUnitId), ownerUnitId);
            SetProperty(message, nameof(ClientLootItem.LootUnitId), lootUnitId);
            SetProperty(message, nameof(ClientLootItem.Request), request);
            return message;
        }

        private static ClientItemUseLootBag CreateLootBagMessage(ulong guid)
        {
            var message = new ClientItemUseLootBag();
            SetProperty(message, nameof(ClientItemUseLootBag.Guid), guid);
            return message;
        }

        private static void SetProperty<T>(object instance, string propertyName, T value)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(instance, value);
        }
    }
}
