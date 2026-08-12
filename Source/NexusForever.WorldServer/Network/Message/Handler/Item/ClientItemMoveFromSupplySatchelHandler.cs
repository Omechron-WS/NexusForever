using System.Linq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Item
{
    public class ClientItemMoveFromSupplySatchelHandler : IMessageHandler<IWorldSession, ClientItemMoveFromSupplySatchel>
    {
        public void HandleMessage(IWorldSession session, ClientItemMoveFromSupplySatchel request)
        {
            if (request.Amount == 0u)
                return;

            IPlayer player = session.Player;
            ISupplySatchelManager manager = player.SupplySatchelManager;
            ITradeskillMaterial material = manager.FirstOrDefault(value => value.MaterialId == request.MaterialId);
            if (material == null
                || material.Owner != player.CharacterId
                || material.MaterialId != request.MaterialId
                || material.Amount == 0u
                || request.Amount > material.Amount
                || material.Entry == null
                || material.Entry.Id != material.MaterialId
                || material.Entry.Item2IdStatRevolution == 0u)
                return;

            manager.MoveToInventory(request.MaterialId, request.Amount);
        }
    }
}
