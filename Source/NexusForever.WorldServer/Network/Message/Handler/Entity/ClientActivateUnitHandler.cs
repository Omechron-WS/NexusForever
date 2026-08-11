using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.CSI;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity
{
    public class ClientActivateUnitHandler : IMessageHandler<IWorldSession, ClientActivateUnit>
    {
        public void HandleMessage(IWorldSession session, ClientActivateUnit activateUnit)
        {
            IWorldEntity entity = session.Player.GetVisible<IWorldEntity>(activateUnit.UnitId);
            if (!ClientSideInteractionValidator.IsValid(session.Player, entity)
                || ClientSideInteractionValidator.HasCastActivation(entity))
                throw new InvalidPacketValueException();

            entity.OnActivate(session.Player);
        }
    }
}
