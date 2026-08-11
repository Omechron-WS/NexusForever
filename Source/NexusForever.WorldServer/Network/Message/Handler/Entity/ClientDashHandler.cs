using NexusForever.Game.Spell;
using NexusForever.Game.Static.Entity.Movement;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Entity;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity
{
    public class ClientDashHandler : IMessageHandler<IWorldSession, ClientDash>
    {
        public void HandleMessage(IWorldSession session, ClientDash dash)
        {
            uint spell4Id = dash.Direction switch
            {
                DashDirection.Left    => 25293u,
                DashDirection.Right   => 25294u,
                DashDirection.Forward => 25295u,
                DashDirection.Back    => 25296u,
                _ => throw new InvalidPacketValueException()
            };

            session.Player.CastSpell(spell4Id, new SpellParameters
            {
                UserInitiatedSpellCast = false
            });
        }
    }
}
