using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Housing;

namespace NexusForever.WorldServer.Network.Message.Handler.Housing
{
    public class ClientHousingResidencePrivacyLevelHandler : IMessageHandler<IWorldSession, ClientHousingSetPrivacyLevel>
    {
        public void HandleMessage(IWorldSession session, ClientHousingSetPrivacyLevel housingSetPrivacyLevel)
        {
            if (session.Player.Map is not IResidenceMapInstance)
                throw new InvalidPacketValueException();

            if (session.Player.ResidenceManager.Residence == null)
                throw new InvalidPacketValueException();

            session.Player.ResidenceManager.SetResidencePrivacy(housingSetPrivacyLevel.PrivacyLevel);
        }
    }
}
