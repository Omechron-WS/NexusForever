using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.CSI;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.CSI
{
    public class ClientSideInteraction : IClientSideInteraction
    {
        public uint ClientUniqueId { get; }
        public IWorldEntity ActivateUnit { get; }
        public CSIType CsiType { get; }
        public ClientSideInteractionEntry Entry { get; }

        private readonly IPlayer owner;

        /// <summary>
        /// Create a new CSI for the given player activating the given entity.
        /// Optionally loads the CSI entry from the GameTable via the spell's ClientSideInteractionId.
        /// </summary>
        public ClientSideInteraction(IPlayer owner, IWorldEntity activateUnit, uint clientUniqueId, uint clientSideInteractionId = 0)
        {
            this.owner     = owner ?? throw new ArgumentNullException(nameof(owner));
            ActivateUnit   = activateUnit ?? throw new ArgumentNullException(nameof(activateUnit));
            ClientUniqueId = clientUniqueId;

            if (clientSideInteractionId > 0)
                Entry = GameTableManager.Instance.ClientSideInteraction.GetEntry(clientSideInteractionId);

            CsiType = Entry != null ? (CSIType)Entry.InteractionType : CSIType.Interaction;
        }

        /// <summary>
        /// Called when the client reports CSI success.
        /// </summary>
        public void TriggerSuccess()
        {
            ActivateUnit?.OnActivateSuccess(owner);
        }

        /// <summary>
        /// Called when the client reports CSI failure.
        /// </summary>
        public void TriggerFail()
        {
            ActivateUnit?.OnActivateFail(owner);
        }
    }
}
