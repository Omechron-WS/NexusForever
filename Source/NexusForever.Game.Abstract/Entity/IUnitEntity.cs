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
        /// Add or refresh an owned <see cref="Property"/> modifier.
        /// </summary>
        /// <returns><see langword="true"/> when the modifier was applied; otherwise, <see langword="false"/>.</returns>
        bool AddSpellModifierProperty(ISpellPropertyModifier modifier);

        /// <summary>
        /// Remove one exact owned <see cref="Property"/> modifier.
        /// </summary>
        /// <returns><see langword="true"/> when the modifier was present; otherwise, <see langword="false"/>.</returns>
        bool RemoveSpellModifierProperty(Property property, SpellEffectIdentity identity);

        /// <summary>
        /// Remove all spell-owned <see cref="Property"/> modifiers affecting this <see cref="IUnitEntity"/>.
        /// </summary>
        void ClearSpellModifierProperties();

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
        /// Returns the first active spell matching the supplied predicate.
        /// </summary>
        ISpell GetActiveSpell(Func<ISpell, bool> predicate);

        /// <summary>
        /// Returns the active spell with the supplied server casting identifier.
        /// </summary>
        ISpell GetActiveSpell(uint castingId);

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
        /// <param name="attacker">Entity dealing the damage.</param>
        /// <param name="damageDescription">Calculated damage to apply.</param>
        /// <param name="triggerProcs">Whether this damage can dispatch damage proc events.</param>
        void TakeDamage(IUnitEntity attacker, IDamageDescription damageDescription, bool triggerProcs = true);

        /// <summary>
        /// Modify the health of this <see cref="IUnitEntity"/> by the supplied amount.
        /// </summary>
        /// <remarks>
        /// If the <see cref="DamageType"/> is <see cref="DamageType.Heal"/> amount is added to current health otherwise subtracted.
        /// </remarks>
        void ModifyHealth(uint amount, DamageType type, IUnitEntity source);

        /// <summary>
        /// Register a proc on this entity, rejecting a duplicate effect entry for the same event type.
        /// </summary>
        bool ApplyProc(IProcInfo proc);

        /// <summary>
        /// Remove a proc from this entity.
        /// </summary>
        bool RemoveProc(IProcInfo proc);

        /// <summary>
        /// Dispatch a proc event to all matching procs on this entity.
        /// </summary>
        /// <param name="type">Combat event that occurred.</param>
        /// <param name="primaryTarget">Opposing unit involved in the event, if any.</param>
        void FireProc(ProcType type, IUnitEntity primaryTarget = null);

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
