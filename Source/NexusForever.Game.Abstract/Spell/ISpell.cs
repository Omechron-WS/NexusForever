using NexusForever.Game.Abstract.Combat;
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
        /// Apply and track a property modifier owned by this spell.
        /// </summary>
        /// <returns><see langword="true"/> when the target accepted the modifier; otherwise, <see langword="false"/>.</returns>
        bool ApplyPropertyModifier(IUnitEntity target, ISpellPropertyModifier modifier);

        /// <summary>
        /// Track a proc applied by this spell so it can be removed with the spell's effects.
        /// </summary>
        void TrackProc(IUnitEntity target, IProcInfo proc);

        /// <summary>
        /// Post-tick update for state transitions and cleanup.
        /// </summary>
        void LateUpdate(double lastTick);

        bool IsMovingInterrupted();
    }
}
