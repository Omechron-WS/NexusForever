using System;
using System.Linq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Utility;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity.Player
{
    public class ClientInspectPlayerRequestHandler : IMessageHandler<IWorldSession, ClientInspectPlayerRequest>
    {
        public void HandleMessage(IWorldSession session, ClientInspectPlayerRequest inspectPlayer)
        {
            IPlayer requester = session.Player;
            if (requester == null
                || inspectPlayer.UnitId == requester.Guid
                || !requester.InWorld
                || requester.Map == null)
                return;

            IGridEntity visible = requester.GetVisible<IGridEntity>(inspectPlayer.UnitId);
            if (visible is not IPlayer player
                || ReferenceEquals(player, requester)
                || player.Guid != inspectPlayer.UnitId
                || !player.InWorld
                || !ReferenceEquals(player.Map, requester.Map))
                return;

            bool isSameFaction = player.Faction1 == requester.Faction1;
            bool isSameGroup = requester.GroupAssociation != 0ul
                && player.GroupAssociation == requester.GroupAssociation;
            if (!isSameFaction && !isSameGroup)
                return;

            session.EnqueueMessageEncrypted(new ServerInspectPlayerResponse
            {
                UnitId = player.Guid,
                Items = player.Inventory
                    .Single(b => b.Location == InventoryLocation.Equipped)
                    .Select(i => i.Build())
                    .ToList()
            });
        }
    }
}
