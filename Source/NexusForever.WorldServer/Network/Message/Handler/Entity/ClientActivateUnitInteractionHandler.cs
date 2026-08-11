using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity
{
    /// <summary>
    /// Handles the deferred build-16042 client-side interaction activation packet.
    /// </summary>
    public class ClientActivateUnitInteractionHandler : IMessageHandler<IWorldSession, ClientActivateUnitInteraction>
    {
        public void HandleMessage(IWorldSession session, ClientActivateUnitInteraction message)
        {
            ClientSideInteractionActivationHandler.Handle(
                session,
                message.ActivateUnitId,
                message.ClientUniqueId);
        }
    }
}
