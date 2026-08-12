using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Item;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientItemMoveToSupplySatchelHandlerTests
    {
        private const ulong CharacterId = 42ul;
        private const ulong ItemGuid = 84ul;
        private const uint StackCount = 5u;

        [Theory]
        [InlineData(1u)]
        [InlineData(3u)]
        [InlineData(StackCount)]
        public void PositiveBoundedAmount_IsForwardedUnchanged(uint amount)
        {
            SatchelFixture fixture = CreateFixture();

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(ItemGuid, amount));

            fixture.Inventory.Verify(inventory => inventory.GetItem(ItemGuid), Times.Once);
            fixture.Inventory.Verify(
                inventory => inventory.ItemMoveToSupplySatchel(fixture.Item.Object, amount),
                Times.Once);
        }

        [Fact]
        public void PendingCreateInventoryItem_RemainsAdmitted()
        {
            SatchelFixture fixture = CreateFixture();
            fixture.Item.SetupGet(item => item.PendingCreate).Returns(true);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(ItemGuid, StackCount));

            fixture.Inventory.Verify(
                inventory => inventory.ItemMoveToSupplySatchel(fixture.Item.Object, StackCount),
                Times.Once);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(StackCount + 1u)]
        [InlineData(uint.MaxValue)]
        public void UnaccountedAmount_IsRejectedBeforeConversion(uint amount)
        {
            SatchelFixture fixture = CreateFixture();

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(ItemGuid, amount));

            AssertNoConversion(fixture);
        }

        [Theory]
        [InlineData(SourceFailure.MissingItem)]
        [InlineData(SourceFailure.MismatchedGuid)]
        [InlineData(SourceFailure.MissingOwner)]
        [InlineData(SourceFailure.WrongOwner)]
        [InlineData(SourceFailure.PendingDelete)]
        [InlineData(SourceFailure.ZeroStack)]
        [InlineData(SourceFailure.MissingInfo)]
        [InlineData(SourceFailure.MissingEntry)]
        public void InvalidSource_IsRejectedBeforeConversion(SourceFailure failure)
        {
            SatchelFixture fixture = CreateFixture();
            switch (failure)
            {
                case SourceFailure.MissingItem:
                    fixture.Inventory
                        .Setup(inventory => inventory.GetItem(ItemGuid))
                        .Returns((IItem)null);
                    break;
                case SourceFailure.MismatchedGuid:
                    fixture.Item.SetupGet(item => item.Guid).Returns(ItemGuid + 1ul);
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

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(ItemGuid, 1u));

            AssertNoConversion(fixture);
        }

        [Theory]
        [InlineData(InventoryLocation.Equipped)]
        [InlineData(InventoryLocation.PlayerBank)]
        [InlineData(InventoryLocation.Ability)]
        [InlineData(InventoryLocation.None)]
        public void NonInventorySource_IsRejectedBeforeConversion(InventoryLocation location)
        {
            SatchelFixture fixture = CreateFixture();
            fixture.Item.SetupGet(item => item.Location).Returns(location);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(ItemGuid, 1u));

            AssertNoConversion(fixture);
        }

        private static SatchelFixture CreateFixture()
        {
            var info = new Mock<IItemInfo>();
            info.SetupGet(value => value.Entry).Returns(new Item2Entry
            {
                Id = 126u,
                MaxStackCount = StackCount
            });

            var item = new Mock<IItem>();
            item.SetupGet(value => value.Guid).Returns(ItemGuid);
            item.SetupGet(value => value.CharacterId).Returns(CharacterId);
            item.SetupGet(value => value.Location).Returns(InventoryLocation.Inventory);
            item.SetupGet(value => value.PendingDelete).Returns(false);
            item.SetupGet(value => value.StackCount).Returns(StackCount);
            item.SetupGet(value => value.Info).Returns(info.Object);

            var inventory = new Mock<IInventory>();
            inventory.Setup(value => value.GetItem(ItemGuid)).Returns(item.Object);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            return new SatchelFixture(
                new ClientItemMoveToSupplySatchelHandler(),
                session,
                inventory,
                item,
                info);
        }

        private static ClientItemMoveToSupplySatchel CreateMessage(ulong itemGuid, uint amount)
        {
            var message = new ClientItemMoveToSupplySatchel();
            typeof(ClientItemMoveToSupplySatchel)
                .GetProperty(
                    nameof(ClientItemMoveToSupplySatchel.ItemGuid),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, itemGuid);
            typeof(ClientItemMoveToSupplySatchel)
                .GetProperty(
                    nameof(ClientItemMoveToSupplySatchel.Amount),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, amount);
            return message;
        }

        private static void AssertNoConversion(SatchelFixture fixture)
        {
            fixture.Inventory.Verify(
                inventory => inventory.ItemMoveToSupplySatchel(
                    It.IsAny<IItem>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        public enum SourceFailure
        {
            MissingItem,
            MismatchedGuid,
            MissingOwner,
            WrongOwner,
            PendingDelete,
            ZeroStack,
            MissingInfo,
            MissingEntry
        }

        private sealed record SatchelFixture(
            ClientItemMoveToSupplySatchelHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IInventory> Inventory,
            Mock<IItem> Item,
            Mock<IItemInfo> Info);
    }
}
