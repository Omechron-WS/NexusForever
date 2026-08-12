using System.Numerics;
using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Vendor;

namespace NexusForever.WorldServer.Tests.Vendor
{
    public sealed class VendorBuybackHandlerTests
    {
        private const uint VendorGuid = 77u;
        private const uint BuybackUniqueId = 88u;

        [Theory]
        [InlineData(VendorAuthorityFailure.MissingSelection)]
        [InlineData(VendorAuthorityFailure.Despawned)]
        [InlineData(VendorAuthorityFailure.DifferentMap)]
        [InlineData(VendorAuthorityFailure.NotVisible)]
        [InlineData(VendorAuthorityFailure.OutOfRange)]
        [InlineData(VendorAuthorityFailure.MissingInfo)]
        public void InvalidRetainedVendor_IsRejectedBeforeBuybackOrEconomyAccess(VendorAuthorityFailure failure)
        {
            BuybackFixture fixture = CreateFixture();

            switch (failure)
            {
                case VendorAuthorityFailure.MissingSelection:
                    fixture.Player.SetupGet(value => value.SelectedVendor).Returns((INonPlayerEntity)null);
                    break;
                case VendorAuthorityFailure.Despawned:
                    fixture.Vendor.SetupGet(value => value.InWorld).Returns(false);
                    break;
                case VendorAuthorityFailure.DifferentMap:
                    fixture.Vendor.SetupGet(value => value.Map).Returns(Mock.Of<IBaseMap>());
                    break;
                case VendorAuthorityFailure.NotVisible:
                    fixture.Player.Setup(value => value.GetVisible<IWorldEntity>(VendorGuid)).Returns((IWorldEntity)null);
                    break;
                case VendorAuthorityFailure.OutOfRange:
                    fixture.Vendor.SetupGet(value => value.Position).Returns(new Vector3(6f, 0f, 0f));
                    break;
                case VendorAuthorityFailure.MissingInfo:
                    fixture.Vendor.SetupGet(value => value.VendorInfo).Returns((IVendorInfo)null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage());

            fixture.BuybackManager.Verify(
                value => value.GetItem(It.IsAny<IPlayer>(), It.IsAny<uint>()), Times.Never);
            fixture.Inventory.Verify(
                value => value.GetInventorySlotsRemaining(It.IsAny<InventoryLocation>()), Times.Never);
            AssertNoMutation(fixture);
        }

        [Fact]
        public void MissingBuybackItem_AfterValidVendorRejectsBeforeInventoryOrEconomyAccess()
        {
            BuybackFixture fixture = CreateFixture();
            fixture.BuybackManager
                .Setup(value => value.GetItem(fixture.Player.Object, BuybackUniqueId))
                .Returns((IBuybackItem)null);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage());

            fixture.BuybackManager.Verify(
                value => value.GetItem(fixture.Player.Object, BuybackUniqueId), Times.Once);
            fixture.Inventory.Verify(
                value => value.GetInventorySlotsRemaining(It.IsAny<InventoryLocation>()), Times.Never);
            AssertNoMutation(fixture);
        }

        [Fact]
        public void ValidRetainedVendor_PreservesCapacityAffordabilityChargeItemAndRemovalOrder()
        {
            BuybackFixture fixture = CreateFixture();
            var calls = new List<string>();
            fixture.BuybackManager
                .Setup(value => value.GetItem(fixture.Player.Object, BuybackUniqueId))
                .Callback(() => calls.Add("lookup"))
                .Returns(fixture.BuybackItem.Object);
            fixture.Inventory
                .Setup(value => value.GetInventorySlotsRemaining(InventoryLocation.Inventory))
                .Callback(() => calls.Add("capacity"))
                .Returns(1u);
            fixture.CurrencyManager
                .Setup(value => value.CanAfford(CurrencyType.Credits, 10ul))
                .Callback(() => calls.Add("afford-credits"))
                .Returns(true);
            fixture.CurrencyManager
                .Setup(value => value.CanAfford(CurrencyType.Renown, 20ul))
                .Callback(() => calls.Add("afford-renown"))
                .Returns(true);
            fixture.CurrencyManager
                .Setup(value => value.CurrencySubtractAmount(CurrencyType.Credits, 10ul, false))
                .Callback(() => calls.Add("subtract-credits"));
            fixture.CurrencyManager
                .Setup(value => value.CurrencySubtractAmount(CurrencyType.Renown, 20ul, false))
                .Callback(() => calls.Add("subtract-renown"));
            fixture.Inventory
                .Setup(value => value.AddItem(
                    fixture.Item.Object,
                    InventoryLocation.Inventory,
                    ItemUpdateReason.NoReason))
                .Callback(() => calls.Add("add-item"));
            fixture.BuybackManager
                .Setup(value => value.RemoveItem(fixture.Player.Object, fixture.BuybackItem.Object))
                .Callback(() => calls.Add("remove-buyback"));

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage());

            Assert.Equal(
                new[]
                {
                    "lookup",
                    "capacity",
                    "afford-credits",
                    "afford-renown",
                    "subtract-credits",
                    "subtract-renown",
                    "add-item",
                    "remove-buyback"
                },
                calls);
            fixture.BuybackManager.Verify(
                value => value.GetItem(fixture.Player.Object, BuybackUniqueId), Times.Once);
            fixture.Inventory.Verify(
                value => value.AddItem(
                    fixture.Item.Object,
                    InventoryLocation.Inventory,
                    ItemUpdateReason.NoReason),
                Times.Once);
            fixture.BuybackManager.Verify(
                value => value.RemoveItem(fixture.Player.Object, fixture.BuybackItem.Object), Times.Once);
        }

        private static BuybackFixture CreateFixture()
        {
            var map = new Mock<IBaseMap>();
            var vendorInfo = new Mock<IVendorInfo>();
            var vendor = new Mock<INonPlayerEntity>();
            vendor.SetupGet(value => value.Guid).Returns(VendorGuid);
            vendor.SetupGet(value => value.InWorld).Returns(true);
            vendor.SetupGet(value => value.Map).Returns(map.Object);
            vendor.SetupGet(value => value.Position).Returns(Vector3.One);
            vendor.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                ActivateSpellMaxRange = 5f
            });
            vendor.SetupGet(value => value.VendorInfo).Returns(vendorInfo.Object);

            var item = new Mock<IItem>();
            var buybackItem = new Mock<IBuybackItem>();
            buybackItem.SetupGet(value => value.Item).Returns(item.Object);
            buybackItem.SetupGet(value => value.CurrencyChange).Returns(new List<(CurrencyType, ulong)>
            {
                (CurrencyType.Credits, 10ul),
                (CurrencyType.Renown, 20ul)
            });

            var inventory = new Mock<IInventory>();
            inventory
                .Setup(value => value.GetInventorySlotsRemaining(InventoryLocation.Inventory))
                .Returns(1u);

            var currencyManager = new Mock<ICurrencyManager>();
            currencyManager
                .Setup(value => value.CanAfford(It.IsAny<CurrencyType>(), It.IsAny<ulong>()))
                .Returns(true);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.InWorld).Returns(true);
            player.SetupGet(value => value.Map).Returns(map.Object);
            player.SetupGet(value => value.Position).Returns(Vector3.Zero);
            player.SetupGet(value => value.SelectedVendor).Returns(vendor.Object);
            player.Setup(value => value.GetVisible<IWorldEntity>(VendorGuid)).Returns(vendor.Object);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);
            player.SetupGet(value => value.CurrencyManager).Returns(currencyManager.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var buybackManager = new Mock<IBuybackManager>();
            buybackManager
                .Setup(value => value.GetItem(player.Object, BuybackUniqueId))
                .Returns(buybackItem.Object);

            return new BuybackFixture(
                new ClientBuybackItemFromVendorHandler(buybackManager.Object),
                session,
                player,
                vendor,
                inventory,
                currencyManager,
                buybackManager,
                buybackItem,
                item);
        }

        private static ClientBuybackItemFromVendor CreateMessage()
        {
            var message = new ClientBuybackItemFromVendor();
            typeof(ClientBuybackItemFromVendor)
                .GetProperty(
                    nameof(ClientBuybackItemFromVendor.UniqueId),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, BuybackUniqueId);
            return message;
        }

        private static void AssertNoMutation(BuybackFixture fixture)
        {
            fixture.CurrencyManager.Verify(
                value => value.CanAfford(It.IsAny<CurrencyType>(), It.IsAny<ulong>()), Times.Never);
            fixture.CurrencyManager.Verify(
                value => value.CurrencySubtractAmount(
                    It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
            fixture.Inventory.Verify(
                value => value.AddItem(
                    It.IsAny<IItem>(), It.IsAny<InventoryLocation>(), It.IsAny<ItemUpdateReason>()), Times.Never);
            fixture.BuybackManager.Verify(
                value => value.RemoveItem(It.IsAny<IPlayer>(), It.IsAny<IBuybackItem>()), Times.Never);
        }

        public enum VendorAuthorityFailure
        {
            MissingSelection,
            Despawned,
            DifferentMap,
            NotVisible,
            OutOfRange,
            MissingInfo
        }

        private sealed record BuybackFixture(
            ClientBuybackItemFromVendorHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<INonPlayerEntity> Vendor,
            Mock<IInventory> Inventory,
            Mock<ICurrencyManager> CurrencyManager,
            Mock<IBuybackManager> BuybackManager,
            Mock<IBuybackItem> BuybackItem,
            Mock<IItem> Item);
    }
}
