using System.Reflection;
using System.Numerics;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Network.World.Message.Static;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Vendor;

namespace NexusForever.WorldServer.Tests.Vendor
{
    public sealed class VendorSellHandlerTests
    {
        private const ulong CharacterId = 42ul;
        private const uint VendorGuid = 77u;
        private const uint BagIndex = 7u;

        [Theory]
        [InlineData(VendorAuthorityFailure.Despawned)]
        [InlineData(VendorAuthorityFailure.DifferentMap)]
        [InlineData(VendorAuthorityFailure.NotVisible)]
        [InlineData(VendorAuthorityFailure.OutOfRange)]
        [InlineData(VendorAuthorityFailure.MissingInfo)]
        public void InvalidRetainedVendor_IsRejectedBeforeInventoryOrMutation(VendorAuthorityFailure failure)
        {
            SellFixture fixture = CreateFixture(stackCount: 1u);

            switch (failure)
            {
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

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(InventoryLocation.Inventory, BagIndex, 1u));

            fixture.Inventory.Verify(value => value.GetItem(It.IsAny<ItemLocation>()), Times.Never);
            fixture.VendorInfo.VerifyGet(value => value.SellPriceMultiplier, Times.Never);
            AssertNoMutation(fixture);
        }

        [Theory]
        [InlineData(1u)]
        [InlineData(5u)]
        public void ExactOwnedInventoryStack_IsSoldOnce(uint stackCount)
        {
            SellFixture fixture = CreateFixture(stackCount);

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(InventoryLocation.Inventory, BagIndex, stackCount));

            fixture.CurrencyManager.Verify(
                manager => manager.CurrencyAddAmount(CurrencyType.Credits, 10ul * stackCount, false),
                Times.Once);
            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(
                    It.Is<ItemLocation>(location =>
                        location.Location == InventoryLocation.Inventory
                        && location.BagIndex == BagIndex),
                    ItemUpdateReason.Vendor),
                Times.Once);
            fixture.BuybackManager.Verify(
                manager => manager.AddItem(
                    fixture.Player.Object,
                    fixture.Item.Object,
                    stackCount,
                    It.Is<List<(CurrencyType CurrencyTypeId, ulong CurrencyAmount)>>(change =>
                        change.Count == 1
                        && change[0].CurrencyTypeId == CurrencyType.Credits
                        && change[0].CurrencyAmount == 10ul * stackCount)),
                Times.Once);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(4u)]
        [InlineData(6u)]
        public void NonExactQuantity_IsRejectedBeforeMutation(uint quantity)
        {
            SellFixture fixture = CreateFixture(stackCount: 5u);

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(InventoryLocation.Inventory, BagIndex, quantity));

            AssertNoMutation(fixture);
        }

        [Theory]
        [InlineData(SourceFailure.PacketLocation)]
        [InlineData(SourceFailure.StoredLocation)]
        [InlineData(SourceFailure.StoredBagIndex)]
        [InlineData(SourceFailure.Owner)]
        [InlineData(SourceFailure.PendingDelete)]
        [InlineData(SourceFailure.ZeroStack)]
        [InlineData(SourceFailure.MissingInfo)]
        [InlineData(SourceFailure.MissingEntry)]
        public void InvalidSource_IsRejectedBeforeMutation(SourceFailure failure)
        {
            SellFixture fixture = CreateFixture(stackCount: 5u);
            InventoryLocation packetLocation = InventoryLocation.Inventory;

            switch (failure)
            {
                case SourceFailure.PacketLocation:
                    packetLocation = InventoryLocation.Equipped;
                    break;
                case SourceFailure.StoredLocation:
                    fixture.Item.SetupGet(item => item.Location).Returns(InventoryLocation.PlayerBank);
                    break;
                case SourceFailure.StoredBagIndex:
                    fixture.Item.SetupGet(item => item.BagIndex).Returns(BagIndex + 1u);
                    break;
                case SourceFailure.Owner:
                    fixture.Item.SetupGet(item => item.CharacterId).Returns(CharacterId + 1ul);
                    break;
                case SourceFailure.PendingDelete:
                    fixture.Item.SetupGet(item => item.PendingDelete).Returns(true);
                    break;
                case SourceFailure.ZeroStack:
                    fixture.Item.SetupGet(item => item.StackCount).Returns(0u);
                    break;
                case SourceFailure.MissingInfo:
                    fixture.Item.SetupGet(item => item.Info).Returns((IItemInfo)null);
                    break;
                case SourceFailure.MissingEntry:
                    fixture.Info.SetupGet(info => info.Entry).Returns((Item2Entry)null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(packetLocation, BagIndex, quantity: 5u));

            AssertNoMutation(fixture);
        }

        private static SellFixture CreateFixture(uint stackCount)
        {
            var info = new Mock<IItemInfo>();
            info.SetupGet(value => value.Entry).Returns(new Item2Entry
            {
                CurrencyTypeIdSellToVendor = [],
                CurrencyAmountSellToVendor = []
            });

            var item = new Mock<IItem>();
            item.SetupGet(value => value.Info).Returns(info.Object);
            item.SetupGet(value => value.CharacterId).Returns(CharacterId);
            item.SetupGet(value => value.Location).Returns(InventoryLocation.Inventory);
            item.SetupGet(value => value.BagIndex).Returns(BagIndex);
            item.SetupGet(value => value.StackCount).Returns(stackCount);
            item.SetupGet(value => value.PendingDelete).Returns(false);
            item.Setup(value => value.GetVendorSellAmount(0)).Returns(10u);

            var inventory = new Mock<IInventory>();
            inventory.Setup(value => value.GetItem(It.IsAny<ItemLocation>())).Returns(item.Object);
            inventory
                .Setup(value => value.ItemDelete(It.IsAny<ItemLocation>(), ItemUpdateReason.Vendor))
                .Returns(item.Object);

            var currencyManager = new Mock<ICurrencyManager>();
            var vendorInfo = new Mock<IVendorInfo>();
            vendorInfo.SetupGet(value => value.SellPriceMultiplier).Returns(1f);

            var map = new Mock<IBaseMap>();
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

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);
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
            return new SellFixture(
                new ClientVendorSellHandler(buybackManager.Object),
                session,
                player,
                vendor,
                vendorInfo,
                inventory,
                currencyManager,
                buybackManager,
                item,
                info);
        }

        private static ClientVendorSell CreateMessage(
            InventoryLocation location,
            uint bagIndex,
            uint quantity)
        {
            var message = new ClientVendorSell();
            message.ItemLocation.Location = location;
            message.ItemLocation.BagIndex = bagIndex;
            typeof(ClientVendorSell)
                .GetProperty(
                    nameof(ClientVendorSell.Quantity),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, quantity);
            return message;
        }

        private static void AssertNoMutation(SellFixture fixture)
        {
            fixture.CurrencyManager.Verify(
                manager => manager.CurrencyAddAmount(
                    It.IsAny<CurrencyType>(),
                    It.IsAny<ulong>(),
                    It.IsAny<bool>()),
                Times.Never);
            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(
                    It.IsAny<ItemLocation>(),
                    It.IsAny<ItemUpdateReason>()),
                Times.Never);
            fixture.BuybackManager.Verify(
                manager => manager.AddItem(
                    It.IsAny<IPlayer>(),
                    It.IsAny<IItem>(),
                    It.IsAny<uint>(),
                    It.IsAny<List<(CurrencyType CurrencyTypeId, ulong CurrencyAmount)>>()),
                Times.Never);
        }

        public enum SourceFailure
        {
            PacketLocation,
            StoredLocation,
            StoredBagIndex,
            Owner,
            PendingDelete,
            ZeroStack,
            MissingInfo,
            MissingEntry
        }

        public enum VendorAuthorityFailure
        {
            Despawned,
            DifferentMap,
            NotVisible,
            OutOfRange,
            MissingInfo
        }

        private sealed record SellFixture(
            ClientVendorSellHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<INonPlayerEntity> Vendor,
            Mock<IVendorInfo> VendorInfo,
            Mock<IInventory> Inventory,
            Mock<ICurrencyManager> CurrencyManager,
            Mock<IBuybackManager> BuybackManager,
            Mock<IItem> Item,
            Mock<IItemInfo> Info);
    }
}
