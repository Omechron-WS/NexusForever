using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Housing;

namespace NexusForever.WorldServer.Network.Message.Handler.Item
{
    public class ClientHousingAddItemToCrateHandler : IMessageHandler<IWorldSession, ClientHousingAddItemToCrate>
    {
        #region Dependency Injection

        private readonly IGameTableManager gameTableManager;

        public ClientHousingAddItemToCrateHandler(
            IGameTableManager gameTableManager)
        {
            this.gameTableManager = gameTableManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientHousingAddItemToCrate addItemToCrate)
        {
            IItem item = session.Player.Inventory.GetItem(addItemToCrate.ItemGuid);
            IItemInfo info = item?.Info;
            if (item == null
                || item.Guid != addItemToCrate.ItemGuid
                || item.Location != InventoryLocation.Inventory
                || item.CharacterId != session.Player.CharacterId
                || item.PendingDelete
                || item.StackCount == 0u
                || info?.Entry == null)
                throw new InvalidPacketValueException();

            HousingDecorInfoEntry entry = gameTableManager.HousingDecorInfo.GetEntry(info.Entry.HousingDecorInfoId);
            if (entry == null)
                throw new InvalidPacketValueException();

            // Generic item-use semantics intentionally keep reusable quest items intact. A housing item with
            // that same shape would therefore mint unbounded crate decor, while consuming it here before an
            // independently persisted residence mutation can lose the item. Fail closed until the transfer is
            // owned by one commit-acknowledged inventory-to-residence boundary.
            if (info.Entry.MaxCharges == 0u && info.Entry.MaxStackCount == 1u)
                return;

            if (session.Player.Inventory.ItemUse(item))
                session.Player.ResidenceManager.DecorCreate(entry);
        }
    }
}
