using System.Collections.Generic;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.CSI;
using NexusForever.Game.Static.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.WorldServer.Network.Message.Handler.Vendor
{
    public class ClientVendorSellHandler : IMessageHandler<IWorldSession, ClientVendorSell>
    {
        #region Dependency Injection

        private readonly IBuybackManager buybackManager;

        public ClientVendorSellHandler(
            IBuybackManager buybackManager)
        {
            this.buybackManager = buybackManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientVendorSell vendorSell)
        {
            IPlayer player = session.Player;
            INonPlayerEntity vendor = player.SelectedVendor;
            if (!ClientSideInteractionValidator.IsValid(player, vendor))
                return;

            IVendorInfo vendorInfo = vendor.VendorInfo;
            if (vendorInfo == null)
                return;

            IItem item = player.Inventory.GetItem(vendorSell.ItemLocation);
            if (item == null)
                return;

            IItemInfo info = item.Info;
            if (vendorSell.ItemLocation.Location != InventoryLocation.Inventory
                || item.Location != vendorSell.ItemLocation.Location
                || item.BagIndex != vendorSell.ItemLocation.BagIndex
                || item.CharacterId != player.CharacterId
                || item.PendingDelete
                || item.StackCount == 0u
                || vendorSell.Quantity != item.StackCount
                || info?.Entry == null)
                return;

            float costMultiplier = vendorInfo.SellPriceMultiplier * vendorSell.Quantity;

            // do all sanity checks before modifying currency
            var currencyChange = new List<(CurrencyType CurrencyTypeId, ulong CurrencyAmount)>();
            for (int i = 0; i < info.Entry.CurrencyTypeIdSellToVendor.Length; i++)
            {
                CurrencyType currencyId = info.Entry.CurrencyTypeIdSellToVendor[i];
                if (currencyId == CurrencyType.None)
                    continue;

                ulong currencyAmount = (ulong)(info.Entry.CurrencyAmountSellToVendor[i] * costMultiplier);
                currencyChange.Add((currencyId, currencyAmount));
            }

            // TODO Insert calculation for cost here
            currencyChange.Add((CurrencyType.Credits, (ulong)(item.GetVendorSellAmount(0) * costMultiplier)));

            foreach ((CurrencyType currencyTypeId, ulong currencyAmount) in currencyChange)
                player.CurrencyManager.CurrencyAddAmount(currencyTypeId, currencyAmount);

            // TODO Figure out why this is showing "You deleted [item]"
            IItem soldItem = player.Inventory.ItemDelete(vendorSell.ItemLocation, ItemUpdateReason.Vendor);
            buybackManager.AddItem(player, soldItem, vendorSell.Quantity, currencyChange);
        }
    }
}
