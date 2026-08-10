using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Combat;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Abstract.Entity
{
    /// <summary>
    /// An <see cref="IUnitEntity"/> is an extension to <see cref="IWorldEntity"/> which can cast spells, be targed by spells and participate in combat.
    /// </summary>
    public interface IUnitEntity : IWorldEntity
    {
        float HitRadius { get; }

        /// <summary>
        /// Guid of the <see cref="IWorldEntity"/> currently targeted.
        /// </summary>
        uint? TargetGuid { get; }

        /// <summary>
        /// Determines whether or not this <see cref="IUnitEntity"/> is alive.
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// Determines whether or not this <see cref="IUnitEntity"/> is in combat.
        /// </summary>
        bool InCombat { get; }

        /// <summary>
        /// Current combat lifecycle state.
        /// </summary>
        CombatState CombatState { get; }

        public IThreatManager ThreatManager { get; }

        /// <summary>
        /// Attempts to return the current value for a build-16042 <see cref="Vital"/> identifier.
        /// </summary>
        /// <param name="vital">Vital identifier to read.</param>
        /// <param name="value">Current vital value when supported.</param>
        /// <returns><see langword="true"/> when the vital has supported server-side storage; otherwise, <see langword="false"/>.</returns>
        bool TryGetVitalValue(Vital vital, out float value);

        /// <summary>
        /// Attempts to return the maximum value for a build-16042 <see cref="Vital"/> identifier.
        /// </summary>
        /// <param name="vital">Vital identifier to read.</param>
        /// <param name="maximum">Current maximum vital value when bounded and supported.</param>
        /// <returns><see langword="true"/> when the vital has a finite server-side maximum; otherwise, <see langword="false"/>.</returns>
        bool TryGetVitalMaximum(Vital vital, out float maximum);

        /// <summary>
        /// Attempts to add a signed delta to a build-16042 <see cref="Vital"/> identifier.
        /// </summary>
        /// <remarks>
        /// Positive deltas restore or add to a vital and negative deltas consume or damage it.
        /// Integer-backed vital results are truncated towards zero after clamping. Unsupported identifiers,
        /// non-finite deltas and values that cannot be represented by their backing stat fail without mutation.
        /// Positive Health deltas do not resurrect a dead entity; resurrection must use its dedicated lifecycle.
        /// </remarks>
        /// <param name="vital">Vital identifier to modify.</param>
        /// <param name="delta">Signed amount to add.</param>
        /// <param name="source">Optional entity responsible for the modification.</param>
        /// <returns><see langword="true"/> when the vital is supported and the modification is valid; otherwise, <see langword="false"/>.</returns>
        bool TryModifyVital(Vital vital, float delta, IUnitEntity source = null);

        /// <summary>
        /// Add a <see cref="Property"/> modifier given a Spell4Id and <see cref="ISpellPropertyModifier"/> instance.
        /// </summary>
        void AddSpellModifierProperty(ISpellPropertyModifier modifier, uint spell4Id);

        /// <summary>
        /// Remove a <see cref="Property"/> modifier by a Spell that is currently affecting this <see cref="IUnitEntity"/>.
        /// </summary>
        void RemoveSpellProperty(Property property, uint spell4Id);

        /// <summary>
        /// Remove all <see cref="Property"/> modifiers by a Spell that is currently affecting this <see cref="IUnitEntity"/>
        /// </summary>
        void RemoveSpellProperties(uint spell4Id);

        /// <summary>
        /// Cast a <see cref="ISpell"/> with the supplied spell id and <see cref="ISpellParameters"/>.
        /// </summary>
        void CastSpell(uint spell4Id, ISpellParameters parameters);

        /// <summary>
        /// Cast a <see cref="ISpell"/> with the supplied spell id and return the created spell, or null if no spell was created.
        /// </summary>
#nullable enable
        ISpell? CastSpellTracked(uint spell4Id, ISpellParameters parameters);
#nullable disable

        /// <summary>
        /// Cast a <see cref="ISpell"/> with the supplied spell base id, tier and <see cref="ISpellParameters"/>.
        /// </summary>
        void CastSpell(uint spell4BaseId, byte tier, ISpellParameters parameters);

        /// <summary>
        /// Cast a <see cref="ISpell"/> with the supplied <see cref="ISpellParameters"/>.
        /// </summary>
        void CastSpell(ISpellParameters parameters);

        /// <summary>
        /// Cancel any <see cref="ISpell"/>'s that are interrupted by movement.
        /// </summary>
        void CancelSpellsOnMove();

        /// <summary>
        /// Cancel an <see cref="ISpell"/> based on its casting id.
        /// </summary>
        /// <param name="castingId">Casting ID of the spell to cancel</param>
        void CancelSpellCast(uint castingId);

        /// <summary>
        /// Determine if this <see cref="IUnitEntity"/> can attack supplied <see cref="IUnitEntity"/>.
        /// </summary>
        bool CanAttack(IUnitEntity target);

        /// <summary>
        /// Returns whether or not this <see cref="IUnitEntity"/> is an attackable target.
        /// </summary>
        bool IsValidAttackTarget();

        /// <summary>
        /// Deal damage to this <see cref="IUnitEntity"/> from the supplied <see cref="IUnitEntity"/>.
        /// </summary>
        void TakeDamage(IUnitEntity attacker, IDamageDescription damageDescription);

        /// <summary>
        /// Modify the health of this <see cref="IUnitEntity"/> by the supplied amount.
        /// </summary>
        /// <remarks>
        /// If the <see cref="DamageType"/> is <see cref="DamageType.Heal"/> amount is added to current health otherwise subtracted.
        /// </remarks>
        void ModifyHealth(uint amount, DamageType type, IUnitEntity source);

        /// <summary>
        /// Register a proc on this entity, rejecting a duplicate applicator for the same event type.
        /// </summary>
        bool ApplyProc(IProcInfo proc);

        /// <summary>
        /// Remove a proc from this entity.
        /// </summary>
        bool RemoveProc(IProcInfo proc);

        /// <summary>
        /// Dispatch a proc event to all matching procs on this entity.
        /// </summary>
        void FireProc(ProcType type);

        /// <summary>
        /// Set target to supplied target guid.
        /// </summary>
        /// <remarks>
        /// A null target will clear the current target.
        /// </remarks>
        void SetTarget(uint? target, uint threat = 0u);

        /// <summary>
        /// Set target to supplied <see cref="IUnitEntity"/>.
        /// </summary>
        /// <remarks>
        /// A null target will clear the current target.
        /// </remarks>
        void SetTarget(IWorldEntity target, uint threat = 0u);

        /// <summary>
        /// Invoked when a new <see cref="IHostileEntity"/> is added to the threat list.
        /// </summary>
        void OnThreatAddTarget(IHostileEntity hostile);

        /// <summary>
        /// Invoked when an existing <see cref="IHostileEntity"/> is removed from the threat list.
        /// </summary>
        void OnThreatRemoveTarget(IHostileEntity hostile);

        /// <summary>
        /// Invoked when an existing <see cref="IHostileEntity"/> is update on the threat list.
        /// </summary>
        void OnThreatChange(IHostileEntity hostile);
    }
}
