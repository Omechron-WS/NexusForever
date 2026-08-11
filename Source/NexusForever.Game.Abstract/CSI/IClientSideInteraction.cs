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
        /// May be null for non-CSI activations.
        /// </summary>
        ClientSideInteractionEntry Entry { get; }

        /// <summary>
        /// Returns whether the activating player and target still satisfy the interaction boundary.
        /// </summary>
        /// <returns><see langword="true"/> when the interaction remains valid; otherwise, <see langword="false"/>.</returns>
        bool IsValid();

        /// <summary>
        /// Attempts to suppress generic activation credit for one exact objective already advanced by the
        /// interaction spell while the interaction is still pending.
        /// </summary>
        /// <param name="questObjectiveId">Static quest-objective identifier.</param>
        /// <returns><see langword="true"/> when the objective was recorded; otherwise, <see langword="false"/>.</returns>
        bool TrySuppressActivateEntityObjective(uint questObjectiveId);

        /// <summary>
        /// Called when the client reports CSI success.
        /// Routes to the activating entity's OnActivateSuccess callback.
        /// </summary>
        /// <returns><see langword="true"/> when this call claimed the terminal result; otherwise, <see langword="false"/>.</returns>
        bool TriggerSuccess();

        /// <summary>
        /// Completes a success whose spatial boundary was validated immediately before spell execution.
        /// </summary>
        /// <returns><see langword="true"/> when this call claimed the terminal result; otherwise, <see langword="false"/>.</returns>
        bool CompleteSuccess();

        /// <summary>
        /// Called when the client reports CSI failure.
        /// Routes to the activating entity's OnActivateFail callback.
        /// </summary>
        /// <returns><see langword="true"/> when this call claimed the terminal result; otherwise, <see langword="false"/>.</returns>
        bool TriggerFail();
    }
}
