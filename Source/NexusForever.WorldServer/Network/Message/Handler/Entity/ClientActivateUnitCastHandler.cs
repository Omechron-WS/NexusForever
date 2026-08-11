using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity
{
    public class ClientActivateUnitCastHandler : IMessageHandler<IWorldSession, ClientActivateUnitCast>
    {
        public void HandleMessage(IWorldSession session, ClientActivateUnitCast activateUnitCast)
        {
            ClientSideInteractionActivationHandler.Handle(
                session,
                activateUnitCast.ActivateUnitId,
                activateUnitCast.ClientUniqueId);
        }
    }
}
