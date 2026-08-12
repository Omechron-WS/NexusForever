using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.CSI;
using NexusForever.Game.Static.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.WorldServer.Network.Message.Handler.Vendor
{
    public class ClientBuybackItemFromVendorHandler : IMessageHandler<IWorldSession, ClientBuybackItemFromVendor>
    {
        #region Dependency Injection

        private readonly IBuybackManager buybackManager;

        public ClientBuybackItemFromVendorHandler(
            IBuybackManager buybackManager)
        {
            this.buybackManager = buybackManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientBuybackItemFromVendor buybackItemFromVendor)
        {
            IPlayer player = session.Player;
            INonPlayerEntity vendor = player.SelectedVendor;
            if (!ClientSideInteractionValidator.IsValid(player, vendor)
                || vendor.VendorInfo == null)
                return;

            IBuybackItem buybackItem = buybackManager.GetItem(player, buybackItemFromVendor.UniqueId);
            if (buybackItem == null)
                return;

            //TODO Ensure player has room in inventory
            if (player.Inventory.GetInventorySlotsRemaining(InventoryLocation.Inventory) < 1)
            {
                player.SendGenericError(GenericError.ItemInventoryFull);
                return;
            }

            // do all sanity checks before modifying currency
            foreach ((CurrencyType currencyTypeId, ulong currencyAmount) in buybackItem.CurrencyChange)
                if (!player.CurrencyManager.CanAfford(currencyTypeId, currencyAmount))
                    return;

            foreach ((CurrencyType currencyTypeId, ulong currencyAmount) in buybackItem.CurrencyChange)
                player.CurrencyManager.CurrencySubtractAmount(currencyTypeId, currencyAmount);

            player.Inventory.AddItem(buybackItem.Item, InventoryLocation.Inventory);
            buybackManager.RemoveItem(player, buybackItem);
        }
    }
}
