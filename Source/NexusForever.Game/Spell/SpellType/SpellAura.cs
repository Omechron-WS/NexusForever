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
                SendSpellCastResult(result);
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
                if (Caster is IPlayer p)
                    if (Parameters.SpellInfo.Entry.SpellCoolDown != 0u)
                        p.SpellManager.SetSpellCooldown(Parameters.SpellInfo.Entry.Id, Parameters.SpellInfo.Entry.SpellCoolDown / 1000d);

                CostSpell();
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

            // Re-execute for target re-evaluation
            targets.ForEach(t => t.Effects.Clear());
            Execute();

            // Remove Old targets (exited AoE)
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (targets[i].TargetSelectionState == TargetSelectionState.Old)
                    targets.RemoveAt(i);
            }

            // Handle effect retrigger timers
            foreach (uint effectId in effectRetriggerTimers.Keys.ToList())
            {
                effectRetriggerTimers[effectId] -= auraExecuteTimer.Duration;
                if (effectRetriggerTimers[effectId] <= 0d)
                {
                    Spell4EffectsEntry effect = Parameters.SpellInfo.Effects.FirstOrDefault(e => e.Id == effectId);
                    if (effect != null)
                    {
                        // Reset timer
                        effectRetriggerTimers[effectId] = effect.TickTime / 1000d;
                    }
                }
            }

            auraExecuteTimer.Reset();
        }

        protected override void SelectTargets()
        {
            List<ISpellTargetInfo> previousTargets = targets.ToList();
            targets.Clear();

            // Run base selection (caster + primary + telegraph targets)
            base.SelectTargets();

            // Mark existing targets
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
            return base.IsCastingInternal() && status == SpellStatus.Casting;
        }
    }
}
