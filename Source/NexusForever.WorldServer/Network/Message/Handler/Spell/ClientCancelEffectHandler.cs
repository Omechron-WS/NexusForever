using System;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NLog;

namespace NexusForever.WorldServer.Network.Message.Handler.Spell
{
    public class ClientCancelEffectHandler : IMessageHandler<IWorldSession, ClientCancelEffect>
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public void HandleMessage(IWorldSession session, ClientCancelEffect cancelSpell)
        {
            ISpell spell = session?.Player?.GetActiveSpell(cancelSpell.ServerUniqueId);
            if (spell == null)
                return;

            try
            {
                spell.Finish();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to finish client-cancelled spell {spell.CastingId}.");
            }
        }
    }
}
