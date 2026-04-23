using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.CSI;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Abstract.CSI
{
    public interface IClientSideInteraction
    {
        /// <summary>
        /// Client-assigned unique ID for this interaction.
        /// </summary>
        uint ClientUniqueId { get; }

        /// <summary>
        /// The entity being activated.
        /// </summary>
        IWorldEntity ActivateUnit { get; }

        /// <summary>
        /// The CSI mini-game type.
        /// </summary>
        CSIType CsiType { get; }

        /// <summary>
        /// GameTable entry with CSI parameters (threshold, duration, etc.).
        /// </summary>
        ClientSideInteractionEntry Entry { get; }

        /// <summary>
        /// Called when the CSI is ready for client input.
        /// </summary>
        void TriggerReady();

        /// <summary>
        /// Called when the client reports CSI success.
        /// Routes to the activating entity's OnActivateSuccess callback.
        /// </summary>
        void TriggerSuccess();

        /// <summary>
        /// Called when the client reports CSI failure.
        /// Routes to the activating entity's OnActivateFail callback.
        /// </summary>
        void TriggerFail();
    }
}
