using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.CSI;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.CSI
{
    public class ClientSideInteraction : IClientSideInteraction
    {
        public uint ClientUniqueId { get; }
        public IWorldEntity ActivateUnit { get; }
        public CSIType CsiType { get; private set; }
        public ClientSideInteractionEntry Entry { get; private set; }

        private readonly IPlayer owner;

        public ClientSideInteraction(IPlayer owner, IWorldEntity activateUnit, uint clientUniqueId)
        {
            this.owner     = owner;
            ActivateUnit   = activateUnit;
            ClientUniqueId = clientUniqueId;
        }

        /// <summary>
        /// Set the CSI entry from the GameTable, determining the interaction type and parameters.
        /// </summary>
        public void SetEntry(ClientSideInteractionEntry entry)
        {
            Entry   = entry;
            CsiType = entry != null ? (CSIType)entry.InteractionType : CSIType.Interaction;
        }

        /// <summary>
        /// Called when the CSI is ready for client input.
        /// </summary>
        public void TriggerReady()
        {
            // Placeholder for future timer-based triggers
        }

        /// <summary>
        /// Called when the client reports CSI success.
        /// </summary>
        public void TriggerSuccess()
        {
            ActivateUnit.OnActivateSuccess(owner);
        }

        /// <summary>
        /// Called when the client reports CSI failure.
        /// </summary>
        public void TriggerFail()
        {
            ActivateUnit.OnActivateFail(owner);
        }
    }
}
