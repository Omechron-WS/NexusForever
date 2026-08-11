using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Combat;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Combat
{
    public interface IProcInfo : IUpdate
    {
        /// <summary>
        /// Entity that owns this proc.
        /// </summary>
        IUnitEntity Owner { get; }

        /// <summary>
        /// Unique build-16042 spell-effect entry that registered this proc.
        /// </summary>
        uint EffectId { get; }

        /// <summary>
        /// The spell that applied this proc.
        /// </summary>
        uint ApplicatorSpell4Id { get; }

        /// <summary>
        /// The type of event that triggers this proc.
        /// </summary>
        ProcType Type { get; }

        /// <summary>
        /// The spell to cast when the proc triggers.
        /// </summary>
        uint TriggerSpell4Id { get; }

        /// <summary>
        /// Probability that an otherwise eligible event triggers this proc.
        /// </summary>
        float Chance { get; }

        /// <summary>
        /// Returns whether the proc can currently be triggered.
        /// </summary>
        bool CanTrigger { get; }

        /// <summary>
        /// Schedule the proc's spell against the supplied event target.
        /// </summary>
        /// <param name="primaryTarget">Opposing unit involved in the triggering combat event, if any.</param>
        /// <returns>
        /// <see langword="true"/> when the chance roll succeeds and the trigger is scheduled;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        bool Trigger(IUnitEntity primaryTarget = null);

        /// <summary>
        /// Cancel any pending trigger and finish spells previously triggered by this proc.
        /// </summary>
        void Cancel();
    }
}
