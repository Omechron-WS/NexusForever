using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Shared.Game;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    [SpellType(CastMethod.Aura)]
    public class SpellAura : Spell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly UpdateTimer auraTargetTimer = new(0.1d);
        private readonly HashSet<Spell4EffectsEntry> activeNonTickEffects = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Spell4EffectsEntry, HashSet<IUnitEntity>> appliedTargets = new(ReferenceEqualityComparer.Instance);
        private bool telegraphsInitialised;

        public SpellAura(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        public override void Cast()
        {
            base.Cast();
            if (status != SpellStatus.Casting)
                return;

            uint spellDuration = Parameters.SpellInfo.Entry.SpellDuration;
            if (spellDuration is > 0u and < uint.MaxValue)
                events.EnqueueEvent(new SpellEvent(spellDuration / 1000d, Finish));

            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started as aura.");
        }

        public override void Update(double lastTick)
        {
            if (!IsValidUpdateDelta(lastTick))
                return;

            base.Update(lastTick);

            if (status != SpellStatus.Executing)
                return;

            auraTargetTimer.Update(lastTick);
            if (!auraTargetTimer.HasElapsed)
                return;

            RefreshTargets();
            if (activeNonTickEffects.Count != 0)
                ExecuteEffects(activeNonTickEffects);

            auraTargetTimer.Reset();
        }

        protected override void RefreshTargets()
        {
            foreach (ISpellTargetInfo target in targets)
                target.Effects.Clear();

            SelectTargets();
        }

        protected override bool UsesDynamicEffectTargets => true;

        protected override bool CanApplyEffect(Spell4EffectsEntry effect, ISpellTargetInfo target)
        {
            if (target.TargetSelectionState == TargetSelectionState.Old)
                return false;

            if (effect.TickTime > 0u)
            {
                return (target.TargetSelectionState is TargetSelectionState.New or TargetSelectionState.Existing)
                    && base.CanApplyEffect(effect, target);
            }

            bool hasUnattemptedOccupancy = activeNonTickEffects.Contains(effect)
                && (!appliedTargets.TryGetValue(effect, out HashSet<IUnitEntity> targetsForEffect)
                    || !targetsForEffect.Contains(target.Entity));
            return hasUnattemptedOccupancy && base.CanApplyEffect(effect, target);
        }

        protected override void OnEffectActivated(Spell4EffectsEntry effect)
        {
            if (effect.TickTime == 0u)
                activeNonTickEffects.Add(effect);
        }

        protected override void OnEffectAttempted(Spell4EffectsEntry effect, ISpellTargetInfo target)
        {
            if (effect.TickTime > 0u)
                return;

            if (!appliedTargets.TryGetValue(effect, out HashSet<IUnitEntity> targetsForEffect))
            {
                targetsForEffect = new HashSet<IUnitEntity>(ReferenceEqualityComparer.Instance);
                appliedTargets.Add(effect, targetsForEffect);
            }

            targetsForEffect.Add(target.Entity);
        }

        protected override void OnEffectExpired(Spell4EffectsEntry effect)
        {
            base.OnEffectExpired(effect);
            activeNonTickEffects.Remove(effect);
            appliedTargets.Remove(effect);
        }

        protected override void SelectTargets()
        {
            List<ISpellTargetInfo> previousTargets = targets.ToList();
            targets.Clear();

            // Initialise telegraphs once because an aura refreshes its target set continuously.
            if (!telegraphsInitialised && Caster is IPlayer)
            {
                InitialiseTelegraphs();
                telegraphsInitialised = true;
            }

            SpellEffectTargetFlags casterFlags = SpellEffectTargetFlags.Caster;
            if (Parameters.PrimaryTargetId == 0u)
                casterFlags |= SpellEffectTargetFlags.Target;

            targets.Add(new SpellTargetInfo(casterFlags, Caster));

            if (Parameters.PrimaryTargetId != 0u)
            {
                IUnitEntity primaryTargetEntity = Caster.GetVisible<IUnitEntity>(Parameters.PrimaryTargetId);
                if (primaryTargetEntity != null)
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Target, primaryTargetEntity));
            }

            foreach (ITelegraph telegraph in telegraphs)
            {
                foreach (IUnitEntity entity in telegraph.GetTargets())
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Telegraph, entity));
            }

            foreach (ISpellTargetInfo previousTarget in previousTargets)
            {
                ISpellTargetInfo existingTarget = targets.FirstOrDefault(target =>
                    ReferenceEquals(target.Entity, previousTarget.Entity)
                    && target.Flags == previousTarget.Flags);

                if (existingTarget != null)
                {
                    existingTarget.TargetSelectionState = TargetSelectionState.Existing;
                    continue;
                }

                // A caster-only target is represented independently from a telegraph target.
                if (previousTarget.Flags.HasFlag(SpellEffectTargetFlags.Caster)
                    && !previousTarget.Flags.HasFlag(SpellEffectTargetFlags.Telegraph))
                    continue;

                previousTarget.TargetSelectionState = TargetSelectionState.Old;
                targets.Add(previousTarget);
            }

            foreach (ISpellTargetInfo oldTarget in targets
                .Where(target => target.TargetSelectionState == TargetSelectionState.Old)
                .ToArray())
            {
                bool entityRemainsTargeted = targets.Any(target =>
                    target.TargetSelectionState != TargetSelectionState.Old
                    && ReferenceEquals(target.Entity, oldTarget.Entity));
                if (!entityRemainsTargeted)
                {
                    RemoveEffects(oldTarget);
                    foreach (HashSet<IUnitEntity> targetsForEffect in appliedTargets.Values)
                        targetsForEffect.Remove(oldTarget.Entity);
                }

                targets.Remove(oldTarget);
            }

            targets.Sort((left, right) => left.TargetSelectionState.CompareTo(right.TargetSelectionState));
        }

        protected override bool IsCastingInternal()
        {
            return status == SpellStatus.Casting || status == SpellStatus.Executing;
        }
    }
}
