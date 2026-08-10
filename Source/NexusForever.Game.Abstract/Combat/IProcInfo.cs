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
        /// Returns whether the proc can currently be triggered.
        /// </summary>
        bool CanTrigger { get; }

        /// <summary>
        /// Schedule the proc's spell. Returns false when a trigger is already pending.
        /// </summary>
        bool Trigger();

        /// <summary>
        /// Cancel any pending trigger and finish spells previously triggered by this proc.
        /// </summary>
        void Cancel();
    }
}
