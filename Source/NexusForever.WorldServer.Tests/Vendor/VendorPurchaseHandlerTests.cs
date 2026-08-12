using System.Reflection;
using System.Runtime.CompilerServices;
using System.Numerics;
using Moq;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Currency;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Vendor;

namespace NexusForever.WorldServer.Tests.Vendor
{
    public sealed class VendorPurchaseHandlerTests
    {
        private const uint VendorIndex = 7u;
        private const uint VendorGuid = 77u;
        private const uint OutputItemId = 100u;
        private const uint ExtraItemId = 200u;
        private const uint DataBackedCurrencyId = 8u;

        [Theory]
        [InlineData(VendorAuthorityFailure.MissingSelection)]
        [InlineData(VendorAuthorityFailure.Despawned)]
        [InlineData(VendorAuthorityFailure.DifferentMap)]
        [InlineData(VendorAuthorityFailure.NotVisible)]
        [InlineData(VendorAuthorityFailure.OutOfRange)]
        [InlineData(VendorAuthorityFailure.MissingInfo)]
        public void InvalidRetainedVendor_IsRejectedBeforeListingOrEconomyAccess(VendorAuthorityFailure failure)
        {
            PurchaseFixture fixture = CreateFixture();

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

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(1u));

            fixture.VendorInfo.Verify(value => value.GetItemAtIndex(It.IsAny<uint>()), Times.Never);
            fixture.ItemManager.Verify(value => value.GetItemInfo(It.IsAny<uint>()), Times.Never);
            AssertNoAffordabilityOrMutation(fixture);
        }

        [Fact]
        public void OrdinaryQuantity_WidensCostBeforeMultiplication()
        {
            PurchaseFixture fixture = CreateFixture(buyPriceMultiplier: 1.25f);
            fixture.Entry.BuyFromVendorStackCount = 2u;
            fixture.Entry.CurrencyAmount[0] = 65_536u;

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(65_536u));

            const ulong expectedCost = 8_589_934_592ul;
            fixture.CurrencyManager.Verify(
                manager => manager.CanAfford(CurrencyType.Credits, expectedCost),
                Times.Once);
            fixture.CurrencyManager.Verify(
                manager => manager.CurrencySubtractAmount(CurrencyType.Credits, expectedCost, false),
                Times.Once);
            fixture.Inventory.Verify(
                inventory => inventory.ItemCreate(
                    InventoryLocation.Inventory,
                    OutputItemId,
                    131_072u,
                    ItemUpdateReason.NoReason,
                    0u),
                Times.Once);
        }

        [Fact]
        public void OrdinaryQuantity_AcceptsTableBackedNonEnumCurrency()
        {
            PurchaseFixture fixture = CreateFixture();
            fixture.Entry.CurrencyTypeId = [(CurrencyType)DataBackedCurrencyId, CurrencyType.None];
            fixture.Entry.CurrencyAmount = [3u, 0u];

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(2u));

            CurrencyType currencyType = (CurrencyType)DataBackedCurrencyId;
            fixture.CurrencyManager.Verify(manager => manager.CanAfford(currencyType, 6ul), Times.Once);
            fixture.CurrencyManager.Verify(
                manager => manager.CurrencySubtractAmount(currencyType, 6ul, false),
                Times.Once);
            fixture.Inventory.Verify(
                inventory => inventory.ItemCreate(
                    InventoryLocation.Inventory,
                    OutputItemId,
                    2u,
                    ItemUpdateReason.NoReason,
                    0u),
                Times.Once);
        }

        [Fact]
        public void SingleExtraCostQuantity_AggregatesTableBackedCurrency()
        {
            PurchaseFixture fixture = CreateFixture();
            SetExtraCost(fixture.VendorItem, 1, ItemExtraCostType.Currency, DataBackedCurrencyId, 3u);
            SetExtraCost(fixture.VendorItem, 2, ItemExtraCostType.Currency, DataBackedCurrencyId, 4u);
            fixture.Entry.BuyFromVendorStackCount = 5u;

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(1u));

            CurrencyType currencyType = (CurrencyType)DataBackedCurrencyId;
            fixture.CurrencyManager.Verify(manager => manager.CanAfford(currencyType, 7ul), Times.Once);
            fixture.CurrencyManager.Verify(
                manager => manager.CurrencySubtractAmount(currencyType, 7ul, false),
                Times.Once);
            fixture.Inventory.Verify(
                inventory => inventory.ItemCreate(
                    InventoryLocation.Inventory,
                    OutputItemId,
                    5u,
                    ItemUpdateReason.NoReason,
                    0u),
                Times.Once);
        }

        [Fact]
        public void SingleExtraCostQuantity_ChargesItemAndAccountCurrency()
        {
            PurchaseFixture fixture = CreateFixture();
            SetExtraCost(fixture.VendorItem, 1, ItemExtraCostType.Item, ExtraItemId, 2u);
            SetExtraCost(
                fixture.VendorItem,
                2,
                ItemExtraCostType.AccountCurrency,
                (uint)AccountCurrencyType.Omnibits,
                3u);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(1u));

            fixture.Inventory.Verify(inventory => inventory.HasItemCount(ExtraItemId, 2u), Times.Once);
            fixture.AccountCurrencyManager.Verify(
                manager => manager.CanAfford(AccountCurrencyType.Omnibits, 3ul),
                Times.Once);
            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(ExtraItemId, 2u, ItemUpdateReason.Vendor),
                Times.Once);
            fixture.AccountCurrencyManager.Verify(
                manager => manager.CurrencySubtractAmount(AccountCurrencyType.Omnibits, 3ul, 0ul),
                Times.Once);
            fixture.Inventory.Verify(
                inventory => inventory.ItemCreate(
                    InventoryLocation.Inventory,
                    OutputItemId,
                    1u,
                    ItemUpdateReason.NoReason,
                    0u),
                Times.Once);
        }

        [Theory]
        [InlineData(2u)]
        [InlineData(uint.MaxValue)]
        public void ExtraCost_NonSingleQuantityIsRejectedBeforeAffordabilityOrMutation(uint quantity)
        {
            PurchaseFixture fixture = CreateFixture();
            SetExtraCost(fixture.VendorItem, 1, ItemExtraCostType.Currency, DataBackedCurrencyId, 1u);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(quantity));

            AssertNoAffordabilityOrMutation(fixture);
        }

        [Fact]
        public void OrdinaryZeroQuantity_IsRejectedBeforeAffordabilityOrMutation()
        {
            PurchaseFixture fixture = CreateFixture();

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(0u));

            AssertNoAffordabilityOrMutation(fixture);
        }

        [Theory]
        [MemberData(nameof(InvalidBuyPriceMultipliers))]
        public void InvalidOrdinaryMultiplier_IsRejectedBeforeAffordabilityOrMutation(float multiplier)
        {
            PurchaseFixture fixture = CreateFixture(multiplier);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(1u));

            AssertNoAffordabilityOrMutation(fixture);
        }

        [Theory]
        [InlineData(ArithmeticFailure.OutputCount)]
        [InlineData(ArithmeticFailure.CreditCost)]
        [InlineData(ArithmeticFailure.DuplicateOrdinaryCost)]
        [InlineData(ArithmeticFailure.DuplicateExtraItemCost)]
        public void Overflow_IsRejectedBeforeAffordabilityOrMutation(ArithmeticFailure failure)
        {
            PurchaseFixture fixture = CreateFixture(buyPriceMultiplier: 2f);
            uint quantity = uint.MaxValue;

            switch (failure)
            {
                case ArithmeticFailure.OutputCount:
                    fixture.Entry.BuyFromVendorStackCount = 2u;
                    break;
                case ArithmeticFailure.CreditCost:
                    fixture.Entry.CurrencyAmount[0] = uint.MaxValue;
                    break;
                case ArithmeticFailure.DuplicateOrdinaryCost:
                    fixture.Entry.CurrencyTypeId = [CurrencyType.Renown, CurrencyType.Renown];
                    fixture.Entry.CurrencyAmount = [uint.MaxValue, uint.MaxValue];
                    break;
                case ArithmeticFailure.DuplicateExtraItemCost:
                    SetExtraCost(fixture.VendorItem, 1, ItemExtraCostType.Item, ExtraItemId, uint.MaxValue);
                    SetExtraCost(fixture.VendorItem, 2, ItemExtraCostType.Item, ExtraItemId, uint.MaxValue);
                    quantity = 1u;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(quantity));

            AssertNoAffordabilityOrMutation(fixture);
        }

        [Theory]
        [InlineData(OrdinaryShapeFailure.MissingCurrencyTypes)]
        [InlineData(OrdinaryShapeFailure.MissingCurrencyAmounts)]
        [InlineData(OrdinaryShapeFailure.WrongLaneCount)]
        [InlineData(OrdinaryShapeFailure.MismatchedLaneCount)]
        [InlineData(OrdinaryShapeFailure.MissingCost)]
        [InlineData(OrdinaryShapeFailure.MissingCurrencyEntry)]
        [InlineData(OrdinaryShapeFailure.InfoIdentity)]
        [InlineData(OrdinaryShapeFailure.EntryIdentity)]
        [InlineData(OrdinaryShapeFailure.ZeroOutputStack)]
        public void InvalidOrdinaryShape_IsRejectedBeforeAffordabilityOrMutation(OrdinaryShapeFailure failure)
        {
            PurchaseFixture fixture = CreateFixture();

            switch (failure)
            {
                case OrdinaryShapeFailure.MissingCurrencyTypes:
                    fixture.Entry.CurrencyTypeId = null;
                    break;
                case OrdinaryShapeFailure.MissingCurrencyAmounts:
                    fixture.Entry.CurrencyAmount = null;
                    break;
                case OrdinaryShapeFailure.WrongLaneCount:
                    fixture.Entry.CurrencyTypeId = [CurrencyType.Credits];
                    fixture.Entry.CurrencyAmount = [10u];
                    break;
                case OrdinaryShapeFailure.MismatchedLaneCount:
                    fixture.Entry.CurrencyAmount = [10u];
                    break;
                case OrdinaryShapeFailure.MissingCost:
                    fixture.Entry.CurrencyTypeId = [CurrencyType.None, CurrencyType.None];
                    break;
                case OrdinaryShapeFailure.MissingCurrencyEntry:
                    fixture.Entry.CurrencyTypeId = [CurrencyType.WarCoin, CurrencyType.None];
                    break;
                case OrdinaryShapeFailure.InfoIdentity:
                    fixture.OutputInfo.SetupGet(info => info.Id).Returns(OutputItemId + 1u);
                    break;
                case OrdinaryShapeFailure.EntryIdentity:
                    fixture.Entry.Id = OutputItemId + 1u;
                    break;
                case OrdinaryShapeFailure.ZeroOutputStack:
                    fixture.Entry.BuyFromVendorStackCount = 0u;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(1u));

            AssertNoAffordabilityOrMutation(fixture);
        }

        [Theory]
        [MemberData(nameof(InvalidExtraCosts))]
        public void InvalidExtraCostShape_IsRejectedBeforeAffordabilityOrMutation(
            ItemExtraCostType type,
            uint id,
            uint quantity)
        {
            PurchaseFixture fixture = CreateFixture();
            SetExtraCost(fixture.VendorItem, 1, type, id, quantity);

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(1u));

            AssertNoAffordabilityOrMutation(fixture);
        }

        public static TheoryData<float> InvalidBuyPriceMultipliers => new()
        {
            0f,
            -1f,
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity,
            float.MaxValue
        };

        public static TheoryData<ItemExtraCostType, uint, uint> InvalidExtraCosts => new()
        {
            { ItemExtraCostType.None, 1u, 0u },
            { ItemExtraCostType.None, 0u, 1u },
            { ItemExtraCostType.Item, 0u, 1u },
            { ItemExtraCostType.Item, ExtraItemId, 0u },
            { ItemExtraCostType.Item, 999u, 1u },
            { ItemExtraCostType.Currency, 0u, 1u },
            { ItemExtraCostType.Currency, (uint)CurrencyType.WarCoin, 1u },
            { ItemExtraCostType.AccountCurrency, 10u, 1u },
            { (ItemExtraCostType)4, 1u, 1u }
        };

        private static PurchaseFixture CreateFixture(float buyPriceMultiplier = 1f)
        {
            var entry = new Item2Entry
            {
                Id = OutputItemId,
                BuyFromVendorStackCount = 1u,
                CurrencyTypeId = [CurrencyType.Credits, CurrencyType.None],
                CurrencyAmount = [10u, 0u]
            };

            var outputInfo = new Mock<IItemInfo>();
            outputInfo.SetupGet(value => value.Id).Returns(OutputItemId);
            outputInfo.SetupGet(value => value.Entry).Returns(entry);
            outputInfo
                .Setup(value => value.GetVendorBuyCurrency(It.IsAny<byte>()))
                .Returns((byte index) => entry.CurrencyTypeId[index]);
            outputInfo
                .Setup(value => value.GetVendorBuyAmount(It.IsAny<byte>()))
                .Returns((byte index) => entry.CurrencyAmount[index]);

            var extraItemEntry = new Item2Entry { Id = ExtraItemId };
            var extraItemInfo = new Mock<IItemInfo>();
            extraItemInfo.SetupGet(value => value.Id).Returns(ExtraItemId);
            extraItemInfo.SetupGet(value => value.Entry).Returns(extraItemEntry);

            var itemManager = new Mock<IItemManager>();
            itemManager
                .Setup(manager => manager.GetItemInfo(It.IsAny<uint>()))
                .Returns((uint id) => id switch
                {
                    OutputItemId => outputInfo.Object,
                    ExtraItemId => extraItemInfo.Object,
                    _ => null
                });

            var vendorItem = new EntityVendorItemModel
            {
                ItemId = OutputItemId,
                ExtraCost1Type = ItemExtraCostType.None,
                ExtraCost2Type = ItemExtraCostType.None
            };

            var vendorInfo = new Mock<IVendorInfo>();
            vendorInfo.SetupGet(value => value.BuyPriceMultiplier).Returns(buyPriceMultiplier);
            vendorInfo.Setup(value => value.GetItemAtIndex(VendorIndex)).Returns(vendorItem);

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

            var inventory = new Mock<IInventory>();
            inventory.Setup(value => value.HasItemCount(It.IsAny<uint>(), It.IsAny<uint>())).Returns(true);

            var currencyManager = new Mock<ICurrencyManager>();
            currencyManager
                .Setup(value => value.CanAfford(It.IsAny<CurrencyType>(), It.IsAny<ulong>()))
                .Returns(true);

            var accountCurrencyManager = new Mock<IAccountCurrencyManager>();
            accountCurrencyManager
                .Setup(value => value.CanAfford(It.IsAny<AccountCurrencyType>(), It.IsAny<ulong>()))
                .Returns(true);

            var account = new Mock<IAccount>();
            account.SetupGet(value => value.CurrencyManager).Returns(accountCurrencyManager.Object);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.InWorld).Returns(true);
            player.SetupGet(value => value.Map).Returns(map.Object);
            player.SetupGet(value => value.Position).Returns(Vector3.Zero);
            player.SetupGet(value => value.SelectedVendor).Returns(vendor.Object);
            player.Setup(value => value.GetVisible<IWorldEntity>(VendorGuid)).Returns(vendor.Object);
            player.SetupGet(value => value.Inventory).Returns(inventory.Object);
            player.SetupGet(value => value.CurrencyManager).Returns(currencyManager.Object);
            player.SetupGet(value => value.Account).Returns(account.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager.SetupGet(value => value.CurrencyType).Returns(CreateGameTable(
                new CurrencyTypeEntry { Id = (uint)CurrencyType.Credits },
                new CurrencyTypeEntry { Id = (uint)CurrencyType.Renown },
                new CurrencyTypeEntry { Id = DataBackedCurrencyId }));
            gameTableManager.SetupGet(value => value.AccountCurrencyType).Returns(CreateGameTable(
                new AccountCurrencyTypeEntry { Id = (uint)AccountCurrencyType.Omnibits }));

            return new PurchaseFixture(
                new ClientVendorPurchaseHandler(itemManager.Object, gameTableManager.Object),
                session,
                player,
                vendor,
                vendorInfo,
                itemManager,
                inventory,
                currencyManager,
                accountCurrencyManager,
                outputInfo,
                entry,
                vendorItem);
        }

        private static ClientVendorPurchase CreateMessage(uint quantity)
        {
            return new ClientVendorPurchase
            {
                VendorIndex = VendorIndex,
                VendorItemQty = quantity
            };
        }

        private static void SetExtraCost(
            EntityVendorItemModel item,
            int slot,
            ItemExtraCostType type,
            uint id,
            uint quantity)
        {
            if (slot == 1)
            {
                item.ExtraCost1Type = type;
                item.ExtraCost1ItemOrCurrencyId = id;
                item.ExtraCost1Quantity = quantity;
            }
            else
            {
                item.ExtraCost2Type = type;
                item.ExtraCost2ItemOrCurrencyId = id;
                item.ExtraCost2Quantity = quantity;
            }
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

        private static void AssertNoAffordabilityOrMutation(PurchaseFixture fixture)
        {
            fixture.CurrencyManager.Verify(
                manager => manager.CanAfford(It.IsAny<CurrencyType>(), It.IsAny<ulong>()),
                Times.Never);
            fixture.AccountCurrencyManager.Verify(
                manager => manager.CanAfford(It.IsAny<AccountCurrencyType>(), It.IsAny<ulong>()),
                Times.Never);
            fixture.Inventory.Verify(
                inventory => inventory.HasItemCount(It.IsAny<uint>(), It.IsAny<uint>()),
                Times.Never);
            fixture.CurrencyManager.Verify(
                manager => manager.CurrencySubtractAmount(
                    It.IsAny<CurrencyType>(),
                    It.IsAny<ulong>(),
                    It.IsAny<bool>()),
                Times.Never);
            fixture.AccountCurrencyManager.Verify(
                manager => manager.CurrencySubtractAmount(
                    It.IsAny<AccountCurrencyType>(),
                    It.IsAny<ulong>(),
                    It.IsAny<ulong>()),
                Times.Never);
            fixture.Inventory.Verify(
                inventory => inventory.ItemDelete(
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<ItemUpdateReason>()),
                Times.Never);
            fixture.Inventory.Verify(
                inventory => inventory.ItemCreate(
                    It.IsAny<InventoryLocation>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<ItemUpdateReason>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        public enum ArithmeticFailure
        {
            OutputCount,
            CreditCost,
            DuplicateOrdinaryCost,
            DuplicateExtraItemCost
        }

        public enum OrdinaryShapeFailure
        {
            MissingCurrencyTypes,
            MissingCurrencyAmounts,
            WrongLaneCount,
            MismatchedLaneCount,
            MissingCost,
            MissingCurrencyEntry,
            InfoIdentity,
            EntryIdentity,
            ZeroOutputStack
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

        private sealed record PurchaseFixture(
            ClientVendorPurchaseHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<INonPlayerEntity> Vendor,
            Mock<IVendorInfo> VendorInfo,
            Mock<IItemManager> ItemManager,
            Mock<IInventory> Inventory,
            Mock<ICurrencyManager> CurrencyManager,
            Mock<IAccountCurrencyManager> AccountCurrencyManager,
            Mock<IItemInfo> OutputInfo,
            Item2Entry Entry,
            EntityVendorItemModel VendorItem);
    }
}
