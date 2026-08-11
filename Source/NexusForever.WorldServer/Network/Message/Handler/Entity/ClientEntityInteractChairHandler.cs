using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.CSI;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity
{
    public class ClientEntityInteractChairHandler : IMessageHandler<IWorldSession, ClientEntityInteractChair>
    {
        private const uint ChairActivationFlag = 0x200000u;

        public void HandleMessage(IWorldSession session, ClientEntityInteractChair entityInteractChair)
        {
            IWorldEntity chair = session.Player.GetVisible<IWorldEntity>(entityInteractChair.ChairUnitId);
            if (!ClientSideInteractionValidator.IsValid(session.Player, chair))
                throw new InvalidPacketValueException();

            if ((chair.CreatureEntry.ActivationFlags & ChairActivationFlag) == 0u)
                throw new InvalidPacketValueException();

            session.Player.Sit(chair);
        }
    }
}
