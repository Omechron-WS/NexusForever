using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Item
{
    public class ClientItemMoveToSupplySatchelHandler : IMessageHandler<IWorldSession, ClientItemMoveToSupplySatchel>
    {
        public void HandleMessage(IWorldSession session, ClientItemMoveToSupplySatchel moveToSupplySatchel)
        {
            IPlayer player = session.Player;
            IItem item = player.Inventory.GetItem(moveToSupplySatchel.ItemGuid);
            IItemInfo info = item?.Info;
            if (item == null
                || item.Guid != moveToSupplySatchel.ItemGuid
                || item.CharacterId != player.CharacterId
                || item.Location != InventoryLocation.Inventory
                || item.PendingDelete
                || item.StackCount == 0u
                || moveToSupplySatchel.Amount == 0u
                || moveToSupplySatchel.Amount > item.StackCount
                || info?.Entry == null)
                return;

            player.Inventory.ItemMoveToSupplySatchel(item, moveToSupplySatchel.Amount);
        }
    }
}
