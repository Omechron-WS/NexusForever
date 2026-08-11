using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.CSI;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity
{
    /// <summary>
    /// Correlates a build-16042 client-side interaction result with the player's active spell.
    /// </summary>
    public class ClientSpellInteractionResultHandler : IMessageHandler<IWorldSession, ClientSpellInteractionResult>
    {
        public void HandleMessage(IWorldSession session, ClientSpellInteractionResult message)
        {
            if (message.Result is not (ClientSideInteractionResult.Fail
                or ClientSideInteractionResult.Success
                or ClientSideInteractionResult.Cancel))
                throw new InvalidPacketValueException();

            ISpell spell = session?.Player?.GetActiveSpell(message.CastingId);
            if (spell == null)
                return;

            if (spell is not IClientSideInteractionSpell interactionSpell)
                throw new InvalidPacketValueException();

            bool accepted = message.Result switch
            {
                ClientSideInteractionResult.Fail    => interactionSpell.FailClientInteraction(),
                ClientSideInteractionResult.Success => interactionSpell.SucceedClientInteraction(),
                ClientSideInteractionResult.Cancel  => interactionSpell.CancelClientInteraction(),
                _                                   => false
            };

            if (!accepted)
                return;

            // Validation is an opaque client field in build 16042. Correlation is based on the
            // server-issued casting ID until evidence establishes an independently verifiable token.
        }
    }
}
