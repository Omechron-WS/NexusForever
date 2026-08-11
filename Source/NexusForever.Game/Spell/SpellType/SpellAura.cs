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

        private readonly UpdateTimer auraExecuteTimer = new(0.1d);
        private readonly Dictionary<uint, double> effectRetriggerTimers = new();
        private bool telegraphsInitialised;

        public SpellAura(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        public override void Cast()
        {
            if (status != SpellStatus.Initiating)
                throw new InvalidOperationException();

            CastResult result = CheckCast();
            if (result != CastResult.Ok)
            {
                FailCast(result);
                return;
            }

            if (Caster is IPlayer player)
                if (Parameters.SpellInfo.GlobalCooldown != null)
                    player.SpellManager.SetGlobalSpellCooldown(Parameters.SpellInfo.GlobalCooldown.CooldownTime / 1000d);

            if (Caster is not IPlayer)
                InitialiseTelegraphs();

            SendSpellStart();

            uint castTime = Parameters.SpellInfo.Entry.CastTime;
            events.EnqueueEvent(new SpellEvent(castTime / 1000d, () =>
            {
                Execute();
            }));

            // Initialise retrigger timers for ticking effects
            foreach (Spell4EffectsEntry effect in Parameters.SpellInfo.Effects)
                if (effect.TickTime > 0)
                    effectRetriggerTimers[effect.Id] = effect.TickTime / 1000d;

            // Schedule finish at spell duration if set
            uint spellDuration = Parameters.SpellInfo.Entry.SpellDuration;
            if (spellDuration > 0 && spellDuration < uint.MaxValue)
                events.EnqueueEvent(new SpellEvent(spellDuration / 1000d, Finish));

            status = SpellStatus.Casting;
            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started as aura.");
        }

        public override void Update(double lastTick)
        {
            base.Update(lastTick);

            if (status != SpellStatus.Executing)
                return;

            auraExecuteTimer.Update(lastTick);
            if (!auraExecuteTimer.HasElapsed)
                return;

            // Re-evaluate targets (New/Existing/Old state tracking)
            targets.ForEach(t => t.Effects.Clear());
            SelectTargets();

            // Apply effects only to New targets (initial application)
            ExecuteEffectsForNewTargets();

            // Remove Old targets (exited AoE)
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (targets[i].TargetSelectionState == TargetSelectionState.Old)
                {
                    RemoveEffects(targets[i]);
                    targets.RemoveAt(i);
                }
            }

            // Handle effect retrigger timers — fire ticking effects when their timer expires
            foreach (uint effectId in effectRetriggerTimers.Keys.ToList())
            {
                effectRetriggerTimers[effectId] -= auraExecuteTimer.Duration;
                if (effectRetriggerTimers[effectId] <= 0d)
                {
                    Spell4EffectsEntry effect = Parameters.SpellInfo.Effects.FirstOrDefault(e => e.Id == effectId);
                    if (effect != null)
                    {
                        ExecuteTickEffect(effect);
                        effectRetriggerTimers[effectId] = effect.TickTime / 1000d;
                    }
                }
            }

            auraExecuteTimer.Reset();
        }

        /// <summary>
        /// Apply non-ticking effects to New targets only (initial application when entering AoE).
        /// </summary>
        private void ExecuteEffectsForNewTargets()
        {
            foreach (Spell4EffectsEntry spell4EffectsEntry in Parameters.SpellInfo.Effects)
            {
                // Skip ticking effects — they fire on their own retrigger timer
                if (spell4EffectsEntry.TickTime > 0)
                    continue;

                List<ISpellTargetInfo> effectTargets = targets
                    .Where(t => t.TargetSelectionState == TargetSelectionState.New
                        && (t.Flags & (SpellEffectTargetFlags)spell4EffectsEntry.TargetFlags) != 0)
                    .ToList();

                SpellEffectDelegate handler = GlobalSpellManager.Instance.GetEffectHandler((SpellEffectType)spell4EffectsEntry.EffectType);
                if (handler == null)
                    continue;

                uint effectId = GlobalSpellManager.Instance.NextEffectId;
                foreach (SpellTargetInfo effectTarget in effectTargets)
                {
                    var info = new SpellTargetInfo.SpellTargetEffectInfo(effectId, spell4EffectsEntry);
                    effectTarget.Effects.Add(info);
                    handler.Invoke(this, effectTarget.Entity, info);
                }
            }
        }

        /// <summary>
        /// Apply a specific ticking effect to both New and Existing targets.
        /// </summary>
        private void ExecuteTickEffect(Spell4EffectsEntry spell4EffectsEntry)
        {
            List<ISpellTargetInfo> effectTargets = targets
                .Where(t => (t.TargetSelectionState == TargetSelectionState.New
                          || t.TargetSelectionState == TargetSelectionState.Existing)
                    && (t.Flags & (SpellEffectTargetFlags)spell4EffectsEntry.TargetFlags) != 0)
                .ToList();

            SpellEffectDelegate handler = GlobalSpellManager.Instance.GetEffectHandler((SpellEffectType)spell4EffectsEntry.EffectType);
            if (handler == null)
                return;

            uint effectId = GlobalSpellManager.Instance.NextEffectId;
            foreach (SpellTargetInfo effectTarget in effectTargets)
            {
                var info = new SpellTargetInfo.SpellTargetEffectInfo(effectId, spell4EffectsEntry);
                effectTarget.Effects.Add(info);
                handler.Invoke(this, effectTarget.Entity, info);
            }
        }

        protected override void SelectTargets()
        {
            List<ISpellTargetInfo> previousTargets = targets.ToList();
            targets.Clear();

            // Initialise telegraphs once (not every tick)
            if (!telegraphsInitialised && Caster is IPlayer)
            {
                InitialiseTelegraphs();
                telegraphsInitialised = true;
            }

            // Add caster
            SpellEffectTargetFlags casterFlags = SpellEffectTargetFlags.Caster;
            if (Parameters.PrimaryTargetId == 0)
                casterFlags |= SpellEffectTargetFlags.Target;

            targets.Add(new SpellTargetInfo(casterFlags, Caster));

            // Add primary target
            if (Parameters.PrimaryTargetId != 0)
            {
                IUnitEntity primaryTargetEntity = Caster.GetVisible<IUnitEntity>(Parameters.PrimaryTargetId);
                if (primaryTargetEntity != null)
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Target, primaryTargetEntity));
            }

            // Add telegraph targets
            foreach (ITelegraph telegraph in telegraphs)
            {
                foreach (IUnitEntity entity in telegraph.GetTargets())
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Telegraph, entity));
            }

            // Mark existing targets by comparing with previous tick
            foreach (ISpellTargetInfo previousTarget in previousTargets)
            {
                ISpellTargetInfo existingTarget = targets.FirstOrDefault(t =>
                    t.Entity.Guid == previousTarget.Entity.Guid && t.Flags == previousTarget.Flags);

                if (existingTarget != null)
                {
                    existingTarget.TargetSelectionState = TargetSelectionState.Existing;
                    continue;
                }

                // Caster-only effects stay (don't mark as Old)
                if (previousTarget.Flags.HasFlag(SpellEffectTargetFlags.Caster)
                    && !previousTarget.Flags.HasFlag(SpellEffectTargetFlags.Telegraph))
                    continue;

                // Target left AoE — mark as Old for removal next tick
                previousTarget.TargetSelectionState = TargetSelectionState.Old;
                targets.Add(previousTarget);
            }

            // Sort: Existing first, then New, then Old
            targets.Sort((a, b) => a.TargetSelectionState.CompareTo(b.TargetSelectionState));
        }

        protected override bool IsCastingInternal()
        {
            return status == SpellStatus.Casting || status == SpellStatus.Executing;
        }
    }
}
