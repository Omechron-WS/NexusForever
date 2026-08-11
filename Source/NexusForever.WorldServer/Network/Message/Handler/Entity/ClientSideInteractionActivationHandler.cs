using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.CSI;
using NexusForever.Network;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity
{
    /// <summary>
    /// Shared validation and dispatch for immediate and deferred activation packets.
    /// </summary>
    internal static class ClientSideInteractionActivationHandler
    {
        public static void Handle(IWorldSession session, uint activateUnitId, uint clientUniqueId)
        {
            IWorldEntity entity = session?.Player?.GetVisible<IWorldEntity>(activateUnitId);
            if (!ClientSideInteractionValidator.IsValid(session?.Player, entity))
                throw new InvalidPacketValueException();

            if (!ClientSideInteractionValidator.HasCastActivation(entity))
                throw new InvalidPacketValueException();

            ISpell activeInteraction = session.Player.GetActiveSpell(spell =>
                spell is IClientSideInteractionSpell);
            if (activeInteraction != null)
                return;

            // A disabled spell, unmet content prerequisite, or failed spell creation is a handled
            // activation failure rather than evidence of a malformed client packet.
            entity.TryActivateCast(session.Player, clientUniqueId);
        }
    }
}
