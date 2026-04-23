using NexusForever.Game.Static.Combat;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Combat
{
    public interface IProcInfo : IUpdate
    {
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
        /// Attempt to trigger the proc. Returns false if on cooldown.
        /// </summary>
        bool Trigger();
    }
}
