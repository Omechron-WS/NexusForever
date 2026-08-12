using System;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.AccountInventory;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Vendor
{
    public class ClientVendorPurchaseHandler : IMessageHandler<IWorldSession, ClientVendorPurchase>
    {
        #region Dependency Injection

        private readonly IItemManager itemManager;
        private readonly IGameTableManager gameTableManager;

        public ClientVendorPurchaseHandler(
            IItemManager itemManager,
            IGameTableManager gameTableManager)
        {
            this.itemManager = itemManager;
            this.gameTableManager = gameTableManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientVendorPurchase vendorPurchase)
        {
            IPlayer player = session.Player;
            IVendorInfo vendorInfo = player.SelectedVendorInfo;
            if (vendorInfo == null)
                return;

            EntityVendorItemModel vendorItem = vendorInfo.GetItemAtIndex(vendorPurchase.VendorIndex);
            if (vendorItem == null)
                return;

            IItemInfo info = itemManager.GetItemInfo(vendorItem.ItemId);
            if (!TryBuildPurchasePlan(
                    vendorInfo,
                    vendorItem,
                    info,
                    vendorPurchase.VendorItemQty,
                    out VendorItemPurchaseCost purchaseCost,
                    out uint outputCount))
                return;

            if (!purchaseCost.CanAfford(player))
                return;

            purchaseCost.Charge(player);
            player.Inventory.ItemCreate(InventoryLocation.Inventory, info.Id, outputCount);
        }

        private bool TryBuildPurchasePlan(
            IVendorInfo vendorInfo,
            EntityVendorItemModel vendorItem,
            IItemInfo info,
            uint quantity,
            out VendorItemPurchaseCost purchaseCost,
            out uint outputCount)
        {
            purchaseCost = null;
            outputCount = 0u;

            if (quantity == 0u
                || info?.Entry == null
                || info.Id != vendorItem.ItemId
                || info.Entry.Id != vendorItem.ItemId
                || info.Entry.BuyFromVendorStackCount == 0u)
                return false;

            if (!IsValidExtraCost(
                    vendorItem.ExtraCost1Type,
                    vendorItem.ExtraCost1ItemOrCurrencyId,
                    vendorItem.ExtraCost1Quantity)
                || !IsValidExtraCost(
                    vendorItem.ExtraCost2Type,
                    vendorItem.ExtraCost2ItemOrCurrencyId,
                    vendorItem.ExtraCost2Quantity))
                return false;

            bool hasExtraCost = vendorItem.ExtraCost1Type != ItemExtraCostType.None
                || vendorItem.ExtraCost2Type != ItemExtraCostType.None;

            // Bulk extra-cost semantics are not yet authoritative. Restrict these listings to one
            // server-described purchase unit so a client quantity cannot amplify a fixed charge.
            if (hasExtraCost && quantity != 1u)
                return false;

            try
            {
                outputCount = checked(quantity * info.Entry.BuyFromVendorStackCount);

                var pendingCost = new VendorItemPurchaseCost();
                if (hasExtraCost)
                {
                    AddExtraCost(
                        pendingCost,
                        vendorItem.ExtraCost1Type,
                        vendorItem.ExtraCost1ItemOrCurrencyId,
                        vendorItem.ExtraCost1Quantity);
                    AddExtraCost(
                        pendingCost,
                        vendorItem.ExtraCost2Type,
                        vendorItem.ExtraCost2ItemOrCurrencyId,
                        vendorItem.ExtraCost2Quantity);
                }
                else if (!TryAddOrdinaryCosts(pendingCost, vendorInfo, info, quantity))
                    return false;

                purchaseCost = pendingCost;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private bool TryAddOrdinaryCosts(
            VendorItemPurchaseCost purchaseCost,
            IVendorInfo vendorInfo,
            IItemInfo info,
            uint quantity)
        {
            CurrencyType[] currencyTypes = info.Entry.CurrencyTypeId;
            uint[] currencyAmounts = info.Entry.CurrencyAmount;
            if (currencyTypes == null
                || currencyAmounts == null
                || currencyTypes.Length != 2
                || currencyAmounts.Length != currencyTypes.Length)
                return false;

            float buyPriceMultiplier = vendorInfo.BuyPriceMultiplier;
            if (!float.IsFinite(buyPriceMultiplier) || buyPriceMultiplier <= 0f)
                return false;

            ulong creditMultiplier = checked((ulong)Math.Ceiling(buyPriceMultiplier));
            bool hasCost = false;
            for (int i = 0; i < currencyTypes.Length; i++)
            {
                CurrencyType currencyType = info.GetVendorBuyCurrency((byte)i);
                if (currencyType == CurrencyType.None)
                    continue;

                uint currencyId = unchecked((uint)currencyType);
                if (gameTableManager.CurrencyType?.GetEntry(currencyId)?.Id != currencyId)
                    return false;

                ulong cost = checked((ulong)info.GetVendorBuyAmount((byte)i) * quantity);
                if (currencyType == CurrencyType.Credits)
                    cost = checked(cost * creditMultiplier);

                purchaseCost.AddCurrencyCost(currencyType, cost);
                hasCost = true;
            }

            return hasCost;
        }

        private bool IsValidExtraCost(ItemExtraCostType type, uint id, uint quantity)
        {
            switch (type)
            {
                case ItemExtraCostType.None:
                    return id == 0u && quantity == 0u;
                case ItemExtraCostType.Item:
                {
                    if (id == 0u || quantity == 0u)
                        return false;

                    IItemInfo costInfo = itemManager.GetItemInfo(id);
                    return costInfo?.Entry != null
                        && costInfo.Id == id
                        && costInfo.Entry.Id == id;
                }
                case ItemExtraCostType.Currency:
                {
                    CurrencyType currencyType = (CurrencyType)id;
                    return id != 0u
                        && quantity != 0u
                        && currencyType != CurrencyType.None
                        && gameTableManager.CurrencyType?.GetEntry(id)?.Id == id;
                }
                case ItemExtraCostType.AccountCurrency:
                {
                    AccountCurrencyType currencyType = (AccountCurrencyType)id;
                    return id != 0u
                        && quantity != 0u
                        && currencyType != AccountCurrencyType.None
                        && gameTableManager.AccountCurrencyType?.GetEntry(id)?.Id == id;
                }
                default:
                    return false;
            }
        }

        private static void AddExtraCost(
            VendorItemPurchaseCost purchaseCost,
            ItemExtraCostType type,
            uint id,
            uint quantity)
        {
            switch (type)
            {
                case ItemExtraCostType.None:
                    break;
                case ItemExtraCostType.Item:
                    purchaseCost.AddItemCost(id, quantity);
                    break;
                case ItemExtraCostType.Currency:
                    purchaseCost.AddCurrencyCost((CurrencyType)id, quantity);
                    break;
                case ItemExtraCostType.AccountCurrency:
                    purchaseCost.AddAccountCurrencyCost((AccountCurrencyType)id, quantity);
                    break;
            }
        }
    }
}
