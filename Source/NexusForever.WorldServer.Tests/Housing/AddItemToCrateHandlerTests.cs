using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model.Housing;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Item;

namespace NexusForever.WorldServer.Tests.Housing
{
    public sealed class AddItemToCrateHandlerTests
    {
        private const ulong CharacterId = 42ul;
        private const ulong ItemGuid = 84ul;
        private const uint DecorId = 126u;

        [Fact]
        public void ReusableSingletonDecor_IsRejectedBeforeInventoryOrResidenceMutation()
        {
            CrateFixture fixture = CreateFixture(maxStackCount: 1u, maxCharges: 0u);

            fixture.Handle();

            AssertNoMutation(fixture);
        }

        [Theory]
        [InlineData(SourceFailure.MissingItem)]
        [InlineData(SourceFailure.WrongGuid)]
        [InlineData(SourceFailure.NonInventory)]
        [InlineData(SourceFailure.WrongOwner)]
        [InlineData(SourceFailure.PendingDelete)]
        [InlineData(SourceFailure.ZeroStack)]
        [InlineData(SourceFailure.MissingInfo)]
        [InlineData(SourceFailure.MissingEntry)]
        [InlineData(SourceFailure.MissingDecor)]
        public void InvalidSource_IsRejectedBeforeInventoryOrResidenceMutation(SourceFailure failure)
        {
            CrateFixture fixture = CreateFixture(maxStackCount: 20u, maxCharges: 0u);
            switch (failure)
            {
                case SourceFailure.MissingItem:
                    fixture.Inventory.Setup(inventory => inventory.GetItem(ItemGuid)).Returns((IItem)null);
                    break;
                case SourceFailure.WrongGuid:
                    fixture.Item.SetupGet(item => item.Guid).Returns(ItemGuid + 1ul);
                    break;
                case SourceFailure.NonInventory:
                    fixture.Item.SetupGet(item => item.Location).Returns(InventoryLocation.PlayerBank);
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
                case SourceFailure.MissingDecor:
                    fixture.GameTableManager
                        .SetupGet(manager => manager.HousingDecorInfo)
                        .Returns(CreateGameTable<HousingDecorInfoEntry>());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }

            Assert.Throws<InvalidPacketValueException>(() => fixture.Handle());

            AssertNoMutation(fixture);
        }

        [Theory]
        [InlineData(20u, 0u)]
        [InlineData(1u, 1u)]
        public void ConsumableDecor_ItemUseThenCreatesExactlyOnce(uint maxStackCount, uint maxCharges)
        {
            CrateFixture fixture = CreateFixture(maxStackCount, maxCharges);
            List<string> events = [];
            fixture.Inventory
                .Setup(inventory => inventory.ItemUse(fixture.Item.Object))
                .Callback(() => events.Add("use"))
                .Returns(true);
            fixture.ResidenceManager
                .Setup(manager => manager.DecorCreate(fixture.DecorEntry, 1u))
                .Callback(() => events.Add("create"));

            fixture.Handle();

            Assert.Equal(["use", "create"], events);
            fixture.Inventory.Verify(inventory => inventory.ItemUse(fixture.Item.Object), Times.Once);
            fixture.ResidenceManager.Verify(
                manager => manager.DecorCreate(fixture.DecorEntry, 1u),
                Times.Once);
        }

        [Fact]
        public void PendingCreateConsumable_RemainsAdmitted()
        {
            CrateFixture fixture = CreateFixture(maxStackCount: 20u, maxCharges: 0u);
            fixture.Item.SetupGet(item => item.PendingCreate).Returns(true);
            fixture.Inventory.Setup(inventory => inventory.ItemUse(fixture.Item.Object)).Returns(true);

            fixture.Handle();

            fixture.Inventory.Verify(inventory => inventory.ItemUse(fixture.Item.Object), Times.Once);
            fixture.ResidenceManager.Verify(
                manager => manager.DecorCreate(fixture.DecorEntry, 1u),
                Times.Once);
        }

        [Fact]
        public void RejectedItemUse_DoesNotCreateDecor()
        {
            CrateFixture fixture = CreateFixture(maxStackCount: 20u, maxCharges: 0u);
            fixture.Inventory.Setup(inventory => inventory.ItemUse(fixture.Item.Object)).Returns(false);

            fixture.Handle();

            fixture.Inventory.Verify(inventory => inventory.ItemUse(fixture.Item.Object), Times.Once);
            fixture.ResidenceManager.Verify(
                manager => manager.DecorCreate(It.IsAny<HousingDecorInfoEntry>(), It.IsAny<uint>()),
                Times.Never);
        }

        private static CrateFixture CreateFixture(uint maxStackCount, uint maxCharges)
        {
            var itemEntry = new Item2Entry
            {
                Id                 = 1u,
                HousingDecorInfoId = DecorId,
                MaxStackCount      = maxStackCount,
                MaxCharges         = maxCharges
            };
            var decorEntry = new HousingDecorInfoEntry { Id = DecorId };

            var info = new Mock<IItemInfo>();
            info.SetupGet(value => value.Entry).Returns(itemEntry);

            var item = new Mock<IItem>();
            item.SetupGet(value => value.Guid).Returns(ItemGuid);
            item.SetupGet(value => value.CharacterId).Returns(CharacterId);
            item.SetupGet(value => value.Location).Returns(InventoryLocation.Inventory);
            item.SetupGet(value => value.StackCount).Returns(1u);
            item.SetupGet(value => value.PendingDelete).Returns(false);
            item.SetupGet(value => value.Info).Returns(info.Object);

            var inventory = new Mock<IInventory>();
            inventory.Setup(value => value.GetItem(ItemGuid)).Returns(item.Object);

            var residenceManager = new Mock<IResidenceManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);
            player.SetupGet(value => value.ResidenceManager).Returns(residenceManager.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager
                .SetupGet(value => value.HousingDecorInfo)
                .Returns(CreateGameTable(decorEntry));

            return new CrateFixture(
                new ClientHousingAddItemToCrateHandler(gameTableManager.Object),
                session,
                player,
                inventory,
                residenceManager,
                gameTableManager,
                item,
                info,
                decorEntry);
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int index = 0; index < entries.Length; index++)
                lookup[(uint)idField.GetValue(entries[index])] = index;

            typeof(GameTable<T>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<T>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = maximumId + 1ul });
            return table;
        }

        private static ClientHousingAddItemToCrate CreateMessage()
        {
            var message = new ClientHousingAddItemToCrate();
            typeof(ClientHousingAddItemToCrate)
                .GetProperty(
                    nameof(ClientHousingAddItemToCrate.ItemGuid),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, ItemGuid);
            return message;
        }

        private static void AssertNoMutation(CrateFixture fixture)
        {
            fixture.Inventory.Verify(
                inventory => inventory.ItemUse(It.IsAny<IItem>()),
                Times.Never);
            fixture.ResidenceManager.Verify(
                manager => manager.DecorCreate(It.IsAny<HousingDecorInfoEntry>(), It.IsAny<uint>()),
                Times.Never);
        }

        public enum SourceFailure
        {
            MissingItem,
            WrongGuid,
            NonInventory,
            WrongOwner,
            PendingDelete,
            ZeroStack,
            MissingInfo,
            MissingEntry,
            MissingDecor
        }

        private sealed record CrateFixture(
            ClientHousingAddItemToCrateHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<IInventory> Inventory,
            Mock<IResidenceManager> ResidenceManager,
            Mock<IGameTableManager> GameTableManager,
            Mock<IItem> Item,
            Mock<IItemInfo> Info,
            HousingDecorInfoEntry DecorEntry)
        {
            public void Handle()
            {
                Handler.HandleMessage(Session.Object, CreateMessage());
            }
        }
    }
}
