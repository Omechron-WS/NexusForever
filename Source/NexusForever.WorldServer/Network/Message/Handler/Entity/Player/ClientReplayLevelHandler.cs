using NexusForever.Game.Spell;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity.Player
{
    public class ClientReplayLevelHandler : IMessageHandler<IWorldSession, ClientReplayLevelUp>
    {
        public void HandleMessage(IWorldSession session, ClientReplayLevelUp request)
        {
            if (request.Level is < 2u or > 50u
                || request.Level > session.Player.Level)
                throw new InvalidPacketValueException();

            session.Player.CastSpell(53378, (byte)(request.Level - 1), new SpellParameters());
        }
    }
}
