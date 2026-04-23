using NexusForever.Game.Abstract.Entity;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared;

namespace NexusForever.Game.Abstract.Spell
{
    public interface ISpell : IDisposable, IUpdate
    {
        ISpellParameters Parameters { get; }
        uint CastingId { get; }
        bool IsCasting { get; }
        bool IsFinished { get; }
        bool IsFinishing { get; }
        bool IsWaiting { get; }

        IUnitEntity Caster { get; }

        /// <summary>
        /// Begin cast, checking prerequisites before initiating.
        /// </summary>
        void Cast();

        /// <summary>
        /// Cancel cast with supplied <see cref="CastResult"/>.
        /// </summary>
        void CancelCast(CastResult result);

        /// <summary>
        /// Force-end the spell and all its effects.
        /// </summary>
        void Finish();

        /// <summary>
        /// Post-tick update for state transitions and cleanup.
        /// </summary>
        void LateUpdate(double lastTick);

        bool IsMovingInterrupted();
    }
}
