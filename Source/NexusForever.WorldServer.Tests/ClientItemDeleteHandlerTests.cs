using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;
using NexusForever.GameTable.Static;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Network.World.Message.Static;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Item;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientItemDeleteHandlerTests
    {
        private const ulong CharacterId = 42ul;
        private const uint BagIndex = 7u;
        private const uint StackCount = 5u;

        [Theory]
        [InlineData(InventoryLocation.Inventory)]
        [InlineData(InventoryLocation.PlayerBank)]
        public void ExactOwnedAdmittedSource_IsDeletedOnce(InventoryLocation location)
        {
            DeleteFixture fixture = CreateFixture(location);

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(location, BagIndex));

            fixture.Inventory.Verify(
                inventory => inventory.GetItem(It.Is<ItemLocation>(from =>
                    from.Location == location
                    && from.BagIndex == BagIndex)),
                Times.Once);
            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(
                    It.Is<ItemLocation>(from =>
                        from.Location == location
                        && from.BagIndex == BagIndex),
                    ItemUpdateReason.Loot),
                Times.Once);
        }

        [Fact]
        public void PendingCreateItem_RemainsAdmitted()
        {
            DeleteFixture fixture = CreateFixture(InventoryLocation.Inventory);
            fixture.Item.SetupGet(item => item.PendingCreate).Returns(true);

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(InventoryLocation.Inventory, BagIndex));

            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(
                    It.IsAny<ItemLocation>(),
                    ItemUpdateReason.Loot),
                Times.Once);
        }

        [Fact]
        public void UnrelatedItemFlags_RemainAdmitted()
        {
            DeleteFixture fixture = CreateFixture(InventoryLocation.Inventory);
            fixture.Entry.Flags = ItemFlags.DestroyOnLogout | ItemFlags.PlayerVsPlayer;

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(InventoryLocation.Inventory, BagIndex));

            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(
                    It.IsAny<ItemLocation>(),
                    ItemUpdateReason.Loot),
                Times.Once);
        }

        [Fact]
        public void CannotBeDeletedFlag_HasNativeValueAndIsRejectedBeforeDeletion()
        {
            Assert.Equal((ItemFlags)0x00010000, ItemFlags.CannotBeDeleted);

            DeleteFixture fixture = CreateFixture(InventoryLocation.Inventory);
            fixture.Entry.Flags = (ItemFlags)0x00010000 | ItemFlags.DestroyOnLogout;

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(
                    fixture.Session.Object,
                    CreateMessage(InventoryLocation.Inventory, BagIndex)));

            AssertNoDeletion(fixture);
        }

        [Fact]
        public void StockReachableEquippedLocation_IsDeliberatelyRejectedBeforePlayerAccess()
        {
            DeleteFixture fixture = CreateFixture(InventoryLocation.Equipped);

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(
                    fixture.Session.Object,
                    CreateMessage(InventoryLocation.Equipped, BagIndex)));

            fixture.Session.VerifyGet(session => session.Player, Times.Never);
            fixture.Inventory.Verify(
                inventory => inventory.GetItem(It.IsAny<ItemLocation>()),
                Times.Never);
            AssertNoDeletion(fixture);
        }

        [Theory]
        [InlineData((InventoryLocation)3)]
        [InlineData(InventoryLocation.Ability)]
        [InlineData(InventoryLocation.Unknown5)]
        [InlineData(InventoryLocation.Unknown8)]
        [InlineData(InventoryLocation.Unknown9)]
        [InlineData(InventoryLocation.Unknown10)]
        [InlineData(InventoryLocation.GuildBankTab1)]
        [InlineData(InventoryLocation.WarPartyBankTab1)]
        [InlineData((InventoryLocation)299)]
        [InlineData((InventoryLocation)300)]
        [InlineData((InventoryLocation)511)]
        [InlineData(InventoryLocation.None)]
        public void NonAdmittedLocation_IsRejectedBeforePlayerAccess(InventoryLocation location)
        {
            DeleteFixture fixture = CreateFixture(InventoryLocation.Inventory);

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(
                    fixture.Session.Object,
                    CreateMessage(location, BagIndex)));

            fixture.Session.VerifyGet(session => session.Player, Times.Never);
            fixture.Inventory.Verify(
                inventory => inventory.GetItem(It.IsAny<ItemLocation>()),
                Times.Never);
            AssertNoDeletion(fixture);
        }

        [Theory]
        [InlineData(InventoryLocation.Inventory)]
        [InlineData(InventoryLocation.PlayerBank)]
        public void NativeBagIndexSentinel_IsRejectedBeforePlayerAccess(InventoryLocation location)
        {
            DeleteFixture fixture = CreateFixture(location);

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(
                    fixture.Session.Object,
                    CreateMessage(location, uint.MaxValue)));

            fixture.Session.VerifyGet(session => session.Player, Times.Never);
            fixture.Inventory.Verify(
                inventory => inventory.GetItem(It.IsAny<ItemLocation>()),
                Times.Never);
            AssertNoDeletion(fixture);
        }

        [Theory]
        [InlineData(LookupFailure.InvalidLocationOrEmptySlot)]
        [InlineData(LookupFailure.OutOfRangeSlot)]
        public void OrdinaryLookupFailure_IsTranslatedBeforeDeletion(LookupFailure failure)
        {
            DeleteFixture fixture = CreateFixture(InventoryLocation.Inventory);
            Exception exception = failure switch
            {
                LookupFailure.InvalidLocationOrEmptySlot => new ArgumentException(),
                LookupFailure.OutOfRangeSlot             => new ArgumentOutOfRangeException(),
                _                                        => throw new ArgumentOutOfRangeException(nameof(failure))
            };
            fixture.Inventory
                .Setup(inventory => inventory.GetItem(It.IsAny<ItemLocation>()))
                .Throws(exception);

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(
                    fixture.Session.Object,
                    CreateMessage(InventoryLocation.Inventory, BagIndex)));

            AssertNoDeletion(fixture);
        }

        [Theory]
        [InlineData(SourceFailure.MissingItem)]
        [InlineData(SourceFailure.StoredLocation)]
        [InlineData(SourceFailure.StoredBagIndex)]
        [InlineData(SourceFailure.MissingOwner)]
        [InlineData(SourceFailure.WrongOwner)]
        [InlineData(SourceFailure.PendingDelete)]
        [InlineData(SourceFailure.ZeroStack)]
        [InlineData(SourceFailure.MissingInfo)]
        [InlineData(SourceFailure.MissingEntry)]
        public void InvalidResolvedSource_IsRejectedBeforeDeletion(SourceFailure failure)
        {
            DeleteFixture fixture = CreateFixture(InventoryLocation.Inventory);
            switch (failure)
            {
                case SourceFailure.MissingItem:
                    fixture.Inventory
                        .Setup(inventory => inventory.GetItem(It.IsAny<ItemLocation>()))
                        .Returns((IItem)null);
                    break;
                case SourceFailure.StoredLocation:
                    fixture.Item.SetupGet(item => item.Location).Returns(InventoryLocation.PlayerBank);
                    break;
                case SourceFailure.StoredBagIndex:
                    fixture.Item.SetupGet(item => item.BagIndex).Returns(BagIndex + 1u);
                    break;
                case SourceFailure.MissingOwner:
                    fixture.Item.SetupGet(item => item.CharacterId).Returns((ulong?)null);
                    break;
                case SourceFailure.WrongOwner:
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

            Assert.Throws<InvalidPacketValueException>(() =>
                fixture.Handler.HandleMessage(
                    fixture.Session.Object,
                    CreateMessage(InventoryLocation.Inventory, BagIndex)));

            AssertNoDeletion(fixture);
        }

        [Fact]
        public void NonArgumentLookupException_IsNotTranslated()
        {
            DeleteFixture fixture = CreateFixture(InventoryLocation.Inventory);
            var expected = new InvalidOperationException();
            fixture.Inventory
                .Setup(inventory => inventory.GetItem(It.IsAny<ItemLocation>()))
                .Throws(expected);

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                fixture.Handler.HandleMessage(
                    fixture.Session.Object,
                    CreateMessage(InventoryLocation.Inventory, BagIndex)));

            Assert.Same(expected, actual);
            AssertNoDeletion(fixture);
        }

        [Fact]
        public void DeletionException_IsNotTranslated()
        {
            DeleteFixture fixture = CreateFixture(InventoryLocation.Inventory);
            var expected = new InvalidOperationException();
            fixture.Inventory
                .Setup(inventory => inventory.ItemDelete(
                    It.IsAny<ItemLocation>(),
                    ItemUpdateReason.Loot))
                .Throws(expected);

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                fixture.Handler.HandleMessage(
                    fixture.Session.Object,
                    CreateMessage(InventoryLocation.Inventory, BagIndex)));

            Assert.Same(expected, actual);
        }

        private static DeleteFixture CreateFixture(InventoryLocation location)
        {
            var entry = new Item2Entry();
            var info = new Mock<IItemInfo>();
            info.SetupGet(value => value.Entry).Returns(entry);

            var item = new Mock<IItem>();
            item.SetupGet(value => value.Info).Returns(info.Object);
            item.SetupGet(value => value.CharacterId).Returns(CharacterId);
            item.SetupGet(value => value.Location).Returns(location);
            item.SetupGet(value => value.BagIndex).Returns(BagIndex);
            item.SetupGet(value => value.PendingDelete).Returns(false);
            item.SetupGet(value => value.StackCount).Returns(StackCount);

            var inventory = new Mock<IInventory>();
            inventory
                .Setup(value => value.GetItem(It.IsAny<ItemLocation>()))
                .Returns(item.Object);
            inventory
                .Setup(value => value.ItemDelete(
                    It.IsAny<ItemLocation>(),
                    ItemUpdateReason.Loot))
                .Returns(item.Object);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            return new DeleteFixture(
                new ClientItemDeleteHandler(),
                session,
                inventory,
                item,
                info,
                entry);
        }

        private static ClientItemDelete CreateMessage(InventoryLocation location, uint bagIndex)
        {
            var message = new ClientItemDelete();
            message.From.Location = location;
            message.From.BagIndex = bagIndex;
            return message;
        }

        private static void AssertNoDeletion(DeleteFixture fixture)
        {
            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(
                    It.IsAny<ItemLocation>(),
                    It.IsAny<ItemUpdateReason>()),
                Times.Never);
        }

        public enum LookupFailure
        {
            InvalidLocationOrEmptySlot,
            OutOfRangeSlot
        }

        public enum SourceFailure
        {
            MissingItem,
            StoredLocation,
            StoredBagIndex,
            MissingOwner,
            WrongOwner,
            PendingDelete,
            ZeroStack,
            MissingInfo,
            MissingEntry
        }

        private sealed record DeleteFixture(
            ClientItemDeleteHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IInventory> Inventory,
            Mock<IItem> Item,
            Mock<IItemInfo> Info,
            Item2Entry Entry);
    }
}
