using System;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Static;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;

namespace NexusForever.WorldServer.Network.Message.Handler.Item
{
    public class ClientItemDeleteHandler : IMessageHandler<IWorldSession, ClientItemDelete>
    {
        public void HandleMessage(IWorldSession session, ClientItemDelete itemDelete)
        {
            ItemLocation from = itemDelete.From;
            if (from.Location != InventoryLocation.Inventory
                && from.Location != InventoryLocation.PlayerBank)
                throw new InvalidPacketValueException();

            if (from.BagIndex == uint.MaxValue)
                throw new InvalidPacketValueException();

            IPlayer player = session.Player;
            IInventory inventory = player.Inventory;
            IItem item;
            try
            {
                item = inventory.GetItem(from);
            }
            catch (ArgumentException)
            {
                throw new InvalidPacketValueException();
            }

            if (item == null
                || item.Location != from.Location
                || item.BagIndex != from.BagIndex
                || item.CharacterId != player.CharacterId
                || item.PendingDelete
                || item.StackCount == 0u)
                throw new InvalidPacketValueException();

            var entry = item.Info?.Entry;
            if (entry == null
                || (entry.Flags & ItemFlags.CannotBeDeleted) != 0)
                throw new InvalidPacketValueException();

            inventory.ItemDelete(from);
        }
    }
}
