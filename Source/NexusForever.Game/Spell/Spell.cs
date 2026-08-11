using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Event;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Combat;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Network.World.Message.Static;
using NexusForever.Script;
using NexusForever.Script.Template.Collection;
using NexusForever.Shared;
using NLog;

namespace NexusForever.Game.Spell
{
    public partial class Spell : ISpell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public ISpellParameters Parameters { get; }
        public uint CastingId { get; }
        public bool IsCasting => IsCastingInternal();
        public bool IsFinished => status == SpellStatus.Finished;
        public bool IsFinishing => status == SpellStatus.Finishing;
        public bool IsWaiting => status == SpellStatus.Waiting;

        public IUnitEntity Caster { get; }

        protected SpellStatus status;

        protected readonly List<ISpellTargetInfo> targets = new();
        protected readonly List<ITelegraph> telegraphs = new();
        private readonly Dictionary<IUnitEntity, List<IProcInfo>> trackedProcs = new(ReferenceEqualityComparer.Instance);

        protected readonly ISpellEventManager events = new SpellEventManager();
        private readonly SpellEffectTimeline effectTimeline;
        private readonly Dictionary<ulong, EffectActivationSnapshot> effectActivationSnapshots = [];
        private readonly HashSet<uint> unsupportedEffectPrerequisitesLogged = [];
        private readonly HashSet<(uint EffectId, uint PrerequisiteId)> failedApplyPrerequisitesLogged = [];

        private IScriptCollection scriptCollection;
        private bool executionCommitted;
        private bool? unsupportedThresholdVitalCost;
        private ulong nextEffectActivationId;

        protected byte currentPhase = 255;

        public Spell(IUnitEntity caster, ISpellParameters parameters)
        {
            Caster     = caster;
            Parameters = parameters;
            CastingId  = GlobalSpellManager.Instance.NextCastingId;
            status     = SpellStatus.Initiating;

            parameters.RootSpellInfo ??= parameters.SpellInfo;
            effectTimeline = new SpellEffectTimeline(parameters.SpellInfo.Entry.SpellDuration);

            scriptCollection = ScriptManager.Instance.InitialiseOwnedScripts<ISpell>(this, parameters.SpellInfo.Entry.Id);
        }

        public void Dispose()
        {
            RemoveAllEffects();

            if (scriptCollection != null)
                ScriptManager.Instance.Unload(scriptCollection);

            scriptCollection = null;
        }

        public virtual void Update(double lastTick)
        {
            scriptCollection.Invoke<IUpdate>(s => s.Update(lastTick));
            ProcessEffectTimeline(effectTimeline.Advance(lastTick));
            events.Update(lastTick);
        }

        /// <summary>
        /// Post-tick update for state transitions and cleanup.
        /// </summary>
        public virtual void LateUpdate(double lastTick)
        {
            if (CanFinish())
            {
                RemoveAllEffects();
                status = SpellStatus.Finished;
                log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has finished.");
            }
        }

        /// <summary>
        /// Returns whether the spell can transition to Finished.
        /// </summary>
        protected virtual bool CanFinish()
        {
            if (status == SpellStatus.Executing
                && !events.HasPendingEvent
                && !effectTimeline.HasPendingEffect)
                return true;

            if (status == SpellStatus.Finishing
                && !events.HasPendingEvent
                && !effectTimeline.HasPendingEffect)
                return true;

            return false;
        }

        /// <summary>
        /// Returns whether the spell is currently casting. Subtypes can override.
        /// </summary>
        protected virtual bool IsCastingInternal()
        {
            return status == SpellStatus.Casting;
        }

        /// <summary>
        /// Begin cast, checking prerequisites before initiating.
        /// </summary>
        public virtual void Cast()
        {
            if (status != SpellStatus.Initiating)
                throw new InvalidOperationException();

            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started initiating.");

            CastResult result = CheckCast();
            if (result != CastResult.Ok)
            {
                FailCast(result);
                return;
            }

            if (Caster is IPlayer player)
                if (Parameters.SpellInfo.GlobalCooldown != null)
                    player.SpellManager.SetGlobalSpellCooldown(Parameters.SpellInfo.GlobalCooldown.CooldownTime / 1000d);

            // It's assumed that non-player entities will be stood still to cast (most do).
            // TODO: There are a handful of telegraphs that are attached to moving units (specifically rotating units) which this needs to be updated to account for.
            if (Caster is not IPlayer)
                InitialiseTelegraphs();

            SendSpellStart();

            // enqueue spell to be executed after cast time
            events.EnqueueEvent(new SpellEvent(Parameters.SpellInfo.Entry.CastTime / 1000d, () => Execute()));
            status = SpellStatus.Casting;

            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started casting.");
        }

        protected virtual CastResult CheckCast()
        {
            CastResult preReqCheck = CheckPrerequisites();
            if (preReqCheck != CastResult.Ok)
                return preReqCheck;

            CastResult ccResult = CheckCCConditions();
            if (ccResult != CastResult.Ok)
                return ccResult;

            if (Caster is IPlayer player)
            {
                if (player.SpellManager.GetSpellCooldown(Parameters.SpellInfo.Entry.Id) > 0d)
                    return CastResult.SpellCooldown;

                // this isn't entirely correct, research GlobalCooldownEnum
                if (Parameters.SpellInfo.Entry.GlobalCooldownEnum == 0
                    && player.SpellManager.GetGlobalSpellCooldown() > 0d)
                    return CastResult.SpellGlobalCooldown;

                if (Parameters.CharacterSpell?.MaxAbilityCharges > 0 && Parameters.CharacterSpell?.AbilityCharges == 0)
                    return CastResult.SpellNoCharges;
            }

            return CheckVitalConditions(VitalCostMode.Validate);
        }

        private CastResult CheckVitalConditions(VitalCostMode costMode)
        {
            Spell4Entry entry = Parameters.SpellInfo.Entry;
            CastResult result = SpellVitalPolicy.CheckCasterRequirements(Caster, entry);
            if (result != CastResult.Ok)
                return result;

            IUnitEntity target = Parameters.PrimaryTargetId == 0u
                ? Caster
                : Caster.GetVisible<IWorldEntity>(Parameters.PrimaryTargetId) as IUnitEntity;
            result = SpellVitalPolicy.CheckTargetRequirement(target, entry);
            if (result != CastResult.Ok)
                return result;

            if (Caster is not IPlayer)
                return CastResult.Ok;

            if (costMode == VitalCostMode.Skip)
                return CastResult.Ok;

            if (HasUnsupportedThresholdVitalCost())
                return CastResult.SpellBad;

            return costMode == VitalCostMode.Consume
                ? SpellVitalPolicy.TryConsumeCosts(Caster, entry)
                : SpellVitalPolicy.CheckCosts(Caster, entry);
        }

        /// <summary>
        /// Return whether this threshold spell declares vital costs which cannot be applied safely by
        /// the current parent-only threshold implementation.
        /// </summary>
        protected virtual bool HasUnsupportedThresholdVitalCost()
        {
            CastMethod castMethod = (CastMethod)Parameters.SpellInfo.BaseInfo.Entry.CastMethod;
            if (castMethod is not (CastMethod.RapidTap or CastMethod.ChargeRelease))
                return false;

            if (unsupportedThresholdVitalCost.HasValue)
                return unsupportedThresholdVitalCost.Value;

            Spell4Entry entry = Parameters.SpellInfo.Entry;
            unsupportedThresholdVitalCost = SpellVitalPolicy.HasCost(entry)
                || GameTableManager.Instance.Spell4Thresholds.Entries.Any(threshold =>
                    threshold.Spell4IdParent == entry.Id && SpellVitalPolicy.HasCost(threshold));
            return unsupportedThresholdVitalCost.Value;
        }

        private CastResult CheckPrerequisites()
        {
            // TODO: Remove below line and evaluate PreReq's for Non-Player Entities
            if (Caster is not IPlayer player)
                return CastResult.Ok;

            if (Parameters.SpellInfo.CasterCastPrerequisite != null && !CheckRunnerOverride(player))
            {
                if (!PrerequisiteManager.Instance.Meets(player, Parameters.SpellInfo.CasterCastPrerequisite.Id))
                    return CastResult.PrereqCasterCast;
            }

            // not sure if this should be for explicit and/or implicit targets
            if (Parameters.SpellInfo.TargetCastPrerequisites != null)
            {
            }

            // this probably isn't the correct place, name implies this should be constantly checked
            if (Parameters.SpellInfo.CasterPersistencePrerequisites != null)
            {
            }

            if (Parameters.SpellInfo.TargetPersistencePrerequisites != null)
            {
            }

            return CastResult.Ok;
        }

        private bool CheckRunnerOverride(IPlayer player)
        {
            foreach (PrerequisiteEntry runnerPrereq in Parameters.SpellInfo.PrerequisiteRunners)
                if (PrerequisiteManager.Instance.Meets(player, runnerPrereq.Id))
                    return true;

            return false;
        }

        private CastResult CheckCCConditions()
        {
            // TODO: this just looks like a mask for CCState enum
            if (Parameters.SpellInfo.CasterCCConditions != null)
            {
            }

            // not sure if this should be for explicit and/or implicit targets
            if (Parameters.SpellInfo.TargetCCConditions != null)
            {
            }

            return CastResult.Ok;
        }

        protected void InitialiseTelegraphs()
        {
            telegraphs.Clear();
            foreach (TelegraphDamageEntry telegraphDamageEntry in Parameters.SpellInfo.Telegraphs)
                telegraphs.Add(new Telegraph(telegraphDamageEntry, Caster, Caster.Position, Caster.Rotation));
        }

        /// <summary>
        /// Cancel cast with supplied <see cref="CastResult"/>.
        /// </summary>
        public virtual void CancelCast(CastResult result)
        {
            if (!IsCasting)
                throw new InvalidOperationException();

            try
            {
                SendSpellCancellation(result);
            }
            finally
            {
                events.CancelEvents();
                try
                {
                    RemoveAllEffects();
                }
                finally
                {
                    status = SpellStatus.Executing;
                }
            }

            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} cast was cancelled.");
        }

        /// <summary>
        /// Force-end the spell and all its effects.
        /// </summary>
        public virtual void Finish()
        {
            if (status == SpellStatus.Finished)
                return;

            events.CancelEvents();
            try
            {
                RemoveAllEffects();
            }
            finally
            {
                status = SpellStatus.Finishing;
            }
        }

        /// <summary>
        /// Track a proc applied by this spell so it can be removed with the spell's effects.
        /// </summary>
        public void TrackProc(IUnitEntity target, IProcInfo proc)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(proc);

            if (!ReferenceEquals(target, proc.Owner))
                throw new ArgumentException("The proc owner does not match the supplied target.", nameof(proc));

            if (!trackedProcs.TryGetValue(target, out List<IProcInfo> procs))
            {
                procs = [];
                trackedProcs.Add(target, procs);
            }

            if (!procs.Contains(proc))
                procs.Add(proc);
        }

        /// <summary>
        /// Remove effects applied to a single spell target.
        /// </summary>
        protected void RemoveEffects(ISpellTargetInfo target)
        {
            if (target?.Entity == null)
                return;

            RemoveTrackedProcs(target.Entity);
        }

        private void RemoveTrackedProcs(IUnitEntity target)
        {
            if (!trackedProcs.Remove(target, out List<IProcInfo> procs))
                return;

            foreach (IProcInfo proc in procs)
                target.RemoveProc(proc);
        }

        private void RemoveTrackedProcs(uint effectId)
        {
            foreach ((IUnitEntity target, List<IProcInfo> procs) in trackedProcs.ToArray())
            {
                foreach (IProcInfo proc in procs.Where(proc => proc.EffectId == effectId).ToArray())
                {
                    target.RemoveProc(proc);
                    procs.Remove(proc);
                }

                if (procs.Count == 0)
                    trackedProcs.Remove(target);
            }
        }

        private void RemoveAllEffects()
        {
            effectTimeline.Cancel();
            effectActivationSnapshots.Clear();

            foreach (IUnitEntity target in trackedProcs.Keys.ToArray())
                RemoveTrackedProcs(target);
        }

        protected virtual void Execute(bool consumeVitalCost = true)
        {
            VitalCostMode initialCostMode = consumeVitalCost && executionCommitted
                ? VitalCostMode.Consume
                : consumeVitalCost
                    ? VitalCostMode.Validate
                    : VitalCostMode.Skip;
            CastResult result = CheckVitalConditions(initialCostMode);
            if (result != CastResult.Ok)
            {
                FailExecution(result);
                return;
            }

            if (!executionCommitted)
            {
                result = CheckExecutionCommit();
                if (result != CastResult.Ok)
                {
                    FailExecution(result);
                    return;
                }

                if (!TryCommitAbilityCharge())
                {
                    FailExecution(CastResult.SpellBad);
                    return;
                }

                if (consumeVitalCost)
                {
                    result = CheckVitalConditions(VitalCostMode.Consume);
                    if (result != CastResult.Ok)
                    {
                        if (Parameters.CharacterSpell?.MaxAbilityCharges > 0u)
                            log.Error($"Spell {Parameters.SpellInfo.Entry.Id} vital transaction failed after its ability charge was committed.");

                        FailExecution(result);
                        return;
                    }
                }

                if (!TryCommitSpellCooldown())
                {
                    FailExecution(CastResult.SpellBad);
                    return;
                }

                executionCommitted = true;
            }

            status = SpellStatus.Executing;
            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started executing.");

            ulong activationId = nextEffectActivationId++;
            try
            {
                RefreshTargets();
                var snapshot = new EffectActivationSnapshot(
                    currentPhase,
                    targets.Select(target => new EffectTargetSnapshot(
                        target.Flags,
                        target.Entity,
                        target.TargetSelectionState)).ToArray());
                effectActivationSnapshots.Add(activationId, snapshot);

                List<Spell4EffectsEntry> registeredEffects = [];
                foreach (Spell4EffectsEntry effect in Parameters.SpellInfo.Effects
                    .Where(IsEffectInCurrentPhase))
                {
                    if (!CanEvaluateApplyPrerequisites(effect))
                    {
                        if (unsupportedEffectPrerequisitesLogged.Add(effect.Id))
                        {
                            log.Warn($"Spell {Parameters.SpellInfo.Entry.Id} effect {effect.Id} declares unsupported effect prerequisite data and was not activated.");
                        }

                        // Persistence and suspend prerequisites govern an already-applied effect and
                        // remain deferred until their lifecycle evaluation is implemented centrally.
                        continue;
                    }

                    if (GlobalSpellManager.Instance.GetEffectHandler(effect.EffectType) == null)
                    {
                        log.Warn($"Unhandled spell effect {effect.EffectType}");
                        continue;
                    }

                    SpellEffectRegistrationResult registration = effectTimeline.Register(
                        effect,
                        currentPhase,
                        activationId);
                    if (registration == SpellEffectRegistrationResult.UnsupportedOpenEnded)
                    {
                        log.Warn($"Spell {Parameters.SpellInfo.Entry.Id} effect {effect.Id} has an open timeline without a finite root duration or client cancellation flag and was not activated.");
                        continue;
                    }

                    registeredEffects.Add(effect);
                }

                IReadOnlyList<SpellEffectTimelineEvent> dueEvents = effectTimeline.Advance(0d);
                SpellEffectActivation[] initialActivations = dueEvents
                    .OfType<SpellEffectActivation>()
                    .Where(activation => activation.ActivationId == activationId)
                    .ToArray();
                ProcessEffectTimeline(dueEvents.Where(timelineEvent =>
                    timelineEvent is not SpellEffectActivation activation
                    || activation.ActivationId != activationId));

                RestoreTargetSnapshot(snapshot);
                PublishInitialEffectSnapshot(
                    registeredEffects,
                    initialActivations.SelectMany(activation => activation.Effects).ToArray(),
                    currentPhase);
                CleanupCompletedEffectSnapshots();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} failed to initialise its effect timeline.");
                FailExecution(CastResult.SpellBad);
            }
        }

        private CastResult CheckExecutionCommit()
        {
            if (Caster is IPlayer player
                && player.SpellManager.GetSpellCooldown(Parameters.SpellInfo.Entry.Id) > 0d)
                return CastResult.SpellCooldown;

            if (Parameters.CharacterSpell?.MaxAbilityCharges > 0u
                && Parameters.CharacterSpell.AbilityCharges == 0u)
                return CastResult.SpellNoCharges;

            return CastResult.Ok;
        }

        private bool TryCommitAbilityCharge()
        {
            ICharacterSpell characterSpell = Parameters.CharacterSpell;
            if (characterSpell?.MaxAbilityCharges is not > 0u)
                return true;

            uint previousCharges = characterSpell.AbilityCharges;
            try
            {
                CostSpell();
                return true;
            }
            catch (Exception exception)
            {
                // CharacterSpell decrements before notifying the client. A notification failure must
                // not turn an already-committed charge into a failed cast or a duplicate retry.
                if (characterSpell.AbilityCharges < previousCharges)
                {
                    log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} committed an ability charge but failed to notify the client.");
                    return true;
                }

                log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} failed to commit an ability charge.");
                return false;
            }
        }

        private bool TryCommitSpellCooldown()
        {
            if (Caster is not IPlayer player || Parameters.SpellInfo.Entry.SpellCoolDown == 0u)
                return true;

            try
            {
                player.SpellManager.SetSpellCooldown(
                    Parameters.SpellInfo.Entry.Id,
                    Parameters.SpellInfo.Entry.SpellCoolDown / 1000d);
                return true;
            }
            catch (Exception exception)
            {
                // SpellManager records the cooldown before sending its packet. Treat a recorded value
                // as committed so an isolated notification failure cannot spend costs without effects.
                if (player.SpellManager.GetSpellCooldown(Parameters.SpellInfo.Entry.Id) > 0d)
                {
                    log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} committed its cooldown but failed to notify the client.");
                    return true;
                }

                log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} failed to commit its cooldown.");
                return false;
            }
        }

        protected void CostSpell()
        {
            if (Parameters.CharacterSpell?.MaxAbilityCharges > 0)
                Parameters.CharacterSpell.UseCharge();
        }

        protected virtual void SelectTargets()
        {
            SpellEffectTargetFlags casterFlags = SpellEffectTargetFlags.Caster;
            if (Parameters.PrimaryTargetId == 0)
                casterFlags |= SpellEffectTargetFlags.Target;

            targets.Add(new SpellTargetInfo(casterFlags, Caster));

            if (Parameters.PrimaryTargetId != 0)
            {
                IUnitEntity primaryTargetEntity = Caster.GetVisible<IWorldEntity>(Parameters.PrimaryTargetId) as IUnitEntity;
                if (primaryTargetEntity != null)
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Target, primaryTargetEntity));
            }

            if (Caster is IPlayer)
                InitialiseTelegraphs();

            foreach (ITelegraph telegraph in telegraphs.Where(IsTelegraphInCurrentPhase))
            {
                foreach (IUnitEntity entity in telegraph.GetTargets())
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Telegraph, entity));
            }
        }

        /// <summary>
        /// Clears prior packet state and selects a fresh target snapshot for one effect activation.
        /// </summary>
        protected virtual void RefreshTargets()
        {
            targets.Clear();
            SelectTargets();
        }

        /// <summary>
        /// Returns whether due activations should refresh membership instead of using registration-time targets.
        /// </summary>
        protected virtual bool UsesDynamicEffectTargets => false;

        /// <summary>
        /// Returns whether an effect may be applied to a target in the current activation snapshot.
        /// </summary>
        protected virtual bool CanApplyEffect(Spell4EffectsEntry effect, ISpellTargetInfo target)
        {
            return MeetsApplyPrerequisite(
                    effect,
                    effect.PrerequisiteIdCasterApply,
                    Caster)
                && MeetsApplyPrerequisite(
                    effect,
                    effect.PrerequisiteIdTargetApply,
                    target.Entity);
        }

        private bool CanEvaluateApplyPrerequisites(Spell4EffectsEntry effect)
        {
            // Persistence and suspend prerequisites govern an already-applied effect. Keep those
            // rows gated until their lifecycle evaluation is implemented centrally.
            if (effect.PrerequisiteIdCasterPersistence != 0u
                || effect.PrerequisiteIdTargetPersistence != 0u
                || effect.PrerequisiteIdTargetSuspend != 0u)
                return false;

            if (effect.PrerequisiteIdCasterApply == 0u
                && effect.PrerequisiteIdTargetApply == 0u)
                return true;

            try
            {
                return (effect.PrerequisiteIdCasterApply == 0u
                        || PrerequisiteManager.Instance.CanEvaluateForUnit(effect.PrerequisiteIdCasterApply))
                    && (effect.PrerequisiteIdTargetApply == 0u
                        || PrerequisiteManager.Instance.CanEvaluateForUnit(effect.PrerequisiteIdTargetApply));
            }
            catch
            {
                return false;
            }
        }

        private bool MeetsApplyPrerequisite(
            Spell4EffectsEntry effect,
            uint prerequisiteId,
            IUnitEntity unit)
        {
            if (prerequisiteId == 0u)
                return true;

            try
            {
                if (PrerequisiteManager.Instance.TryMeets(unit, prerequisiteId, out bool meets))
                    return meets;
            }
            catch (Exception exception)
            {
                LogApplyPrerequisiteFailure(effect, prerequisiteId, exception);
                return false;
            }

            LogApplyPrerequisiteFailure(effect, prerequisiteId, null);
            return false;
        }

        private void LogApplyPrerequisiteFailure(
            Spell4EffectsEntry effect,
            uint prerequisiteId,
            Exception exception)
        {
            if (!failedApplyPrerequisitesLogged.Add((effect.Id, prerequisiteId)))
                return;

            if (exception == null)
            {
                log.Warn($"Spell {Parameters.SpellInfo.Entry.Id} effect {effect.Id} failed to evaluate apply prerequisite {prerequisiteId} and rejected the affected target.");
                return;
            }

            log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} effect {effect.Id} threw while evaluating apply prerequisite {prerequisiteId} and rejected the affected target.");
        }

        /// <summary>
        /// Invoked immediately before a timeline activation selects its targets.
        /// </summary>
        protected virtual void OnEffectActivated(Spell4EffectsEntry effect)
        {
        }

        /// <summary>
        /// Invoked after an effect handler attempts to process one target.
        /// </summary>
        protected virtual void OnEffectAttempted(Spell4EffectsEntry effect, ISpellTargetInfo target)
        {
        }

        /// <summary>
        /// Invoked when a finite effect lifetime ends.
        /// </summary>
        protected virtual void OnEffectExpired(Spell4EffectsEntry effect)
        {
            RemoveTrackedProcs(effect.Id);
        }

        /// <summary>
        /// Applies the supplied activation batch to the current target snapshot.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when the activation transaction committed; otherwise,
        /// <see langword="false"/> after the owning spell entered failure cleanup.
        /// </returns>
        protected bool ExecuteEffects(IEnumerable<Spell4EffectsEntry> effects)
        {
            List<EffectExecutionPlan> plans = BuildEffectExecutionPlans(effects);
            if (!TryCommitEffectCosts(plans))
                return false;

            foreach (EffectExecutionPlan plan in plans.Where(plan => plan.Targets.Count != 0))
            {
                uint effectId = GlobalSpellManager.Instance.NextEffectId;
                foreach (SpellTargetInfo effectTarget in plan.Targets)
                {
                    var info = new SpellTargetInfo.SpellTargetEffectInfo(effectId, plan.Effect);
                    effectTarget.Effects.Add(info);

                    bool handlerSucceeded = TryInvokeEffectHandler(
                        plan.Handler,
                        plan.Effect,
                        effectTarget,
                        info);
                    TrySendSpellGoEffect(effectTarget.Entity, info, handlerSucceeded);
                }
            }

            return true;
        }

        private List<EffectExecutionPlan> BuildEffectExecutionPlans(
            IEnumerable<Spell4EffectsEntry> effects)
        {
            var plans = new List<EffectExecutionPlan>();
            foreach (Spell4EffectsEntry effect in effects)
            {
                SpellEffectDelegate handler = GlobalSpellManager.Instance.GetEffectHandler(effect.EffectType);
                if (handler == null)
                {
                    log.Warn($"Unhandled spell effect {effect.EffectType}");
                    continue;
                }

                List<SpellTargetInfo> effectTargets = targets
                    .Where(target => target.TargetSelectionState != TargetSelectionState.Old)
                    .Where(target => (target.Flags & (SpellEffectTargetFlags)effect.TargetFlags) != 0)
                    .Where(target => CanApplyEffect(effect, target))
                    .Cast<SpellTargetInfo>()
                    .ToList();
                plans.Add(new EffectExecutionPlan(effect, handler, effectTargets));
            }

            return plans;
        }

        private bool TryCommitEffectCosts(IReadOnlyList<EffectExecutionPlan> plans)
        {
            if (Caster is not IPlayer)
                return true;

            Spell4EffectsEntry[] eligibleEffects = plans
                .Where(plan => plan.Targets.Count != 0)
                .Select(plan => plan.Effect)
                .ToArray();
            CastResult result = SpellVitalPolicy.TryConsumeCosts(Caster, eligibleEffects);
            if (result == CastResult.Ok)
                return true;

            FailExecution(result);
            return false;
        }

        private bool IsEffectInCurrentPhase(Spell4EffectsEntry effect)
        {
            CastMethod castMethod = (CastMethod)Parameters.SpellInfo.BaseInfo.Entry.CastMethod;
            if (castMethod != CastMethod.Multiphase || currentPhase == byte.MaxValue)
                return true;

            if (currentPhase >= 32u)
                return false;

            if (effect.PhaseFlags is 1u or uint.MaxValue)
                return true;

            return (effect.PhaseFlags & (1u << currentPhase)) != 0u;
        }

        private bool IsTelegraphInCurrentPhase(ITelegraph telegraph)
        {
            CastMethod castMethod = (CastMethod)Parameters.SpellInfo.BaseInfo.Entry.CastMethod;
            if (castMethod != CastMethod.Multiphase || currentPhase == byte.MaxValue)
                return true;

            if (currentPhase >= 32u)
                return false;

            uint phaseFlags = telegraph.TelegraphDamage.PhaseFlags;
            if (phaseFlags is 1u or uint.MaxValue)
                return true;

            return (phaseFlags & (1u << currentPhase)) != 0u;
        }

        private void PublishInitialEffectSnapshot(
            IReadOnlyList<Spell4EffectsEntry> registeredEffects,
            IReadOnlyList<Spell4EffectsEntry> immediateEffects,
            byte phase)
        {
            foreach (Spell4EffectsEntry effect in immediateEffects)
            {
                try
                {
                    OnEffectActivated(effect);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} effect {effect.Id} activation hook failed.");
                }
            }

            List<EffectExecutionPlan> immediatePlans = BuildEffectExecutionPlans(
                registeredEffects.Where(effect => ContainsReference(immediateEffects, effect)));
            if (!TryCommitEffectCosts(immediatePlans))
                return;

            int immediatePlanIndex = 0;
            foreach (Spell4EffectsEntry effect in registeredEffects)
            {
                bool executeImmediately = ContainsReference(immediateEffects, effect);
                SpellEffectDelegate handler;
                IReadOnlyList<SpellTargetInfo> effectTargets;
                if (executeImmediately)
                {
                    EffectExecutionPlan plan = immediatePlans[immediatePlanIndex++];
                    handler = plan.Handler;
                    effectTargets = plan.Targets;
                }
                else
                {
                    handler = GlobalSpellManager.Instance.GetEffectHandler(effect.EffectType);
                    if (handler == null)
                        continue;

                    effectTargets = targets
                        .Where(target => target.TargetSelectionState != TargetSelectionState.Old)
                        .Where(target => (target.Flags & (SpellEffectTargetFlags)effect.TargetFlags) != 0)
                        .Cast<SpellTargetInfo>()
                        .ToList();
                }

                uint effectId = GlobalSpellManager.Instance.NextEffectId;
                foreach (SpellTargetInfo effectTarget in effectTargets)
                {
                    var info = new SpellTargetInfo.SpellTargetEffectInfo(effectId, effect);
                    effectTarget.Effects.Add(info);

                    if (executeImmediately)
                        TryInvokeEffectHandler(handler, effect, effectTarget, info);
                }
            }

            try
            {
                SendSpellGo(phase);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} failed to publish its initial effect snapshot.");
            }
        }

        private static bool ContainsReference(
            IReadOnlyList<Spell4EffectsEntry> effects,
            Spell4EffectsEntry candidate)
        {
            return effects.Any(effect => ReferenceEquals(effect, candidate));
        }

        private void ProcessEffectTimeline(IEnumerable<SpellEffectTimelineEvent> timelineEvents)
        {
            foreach (SpellEffectTimelineEvent timelineEvent in timelineEvents)
            {
                if (timelineEvent is SpellEffectExpiration expiration)
                {
                    if (!effectTimeline.HasPendingEntry(expiration.Effect))
                    {
                        try
                        {
                            OnEffectExpired(expiration.Effect);
                        }
                        catch (Exception exception)
                        {
                            log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} effect {expiration.Effect.Id} expiration cleanup failed.");
                        }
                    }

                    continue;
                }

                if (timelineEvent is SpellEffectActivation activation)
                    ActivateTimelineSnapshot(activation);
            }

            CleanupCompletedEffectSnapshots();
        }

        private void ActivateTimelineSnapshot(SpellEffectActivation activation)
        {
            if (!effectActivationSnapshots.TryGetValue(
                activation.ActivationId,
                out EffectActivationSnapshot snapshot))
            {
                log.Warn($"Spell {Parameters.SpellInfo.Entry.Id} discarded activation {activation.ActivationId} because its target snapshot is unavailable.");
                return;
            }

            if (snapshot.Phase != activation.Phase)
            {
                log.Warn($"Spell {Parameters.SpellInfo.Entry.Id} discarded activation {activation.ActivationId} because its captured phase does not match.");
                return;
            }

            byte previousPhase = currentPhase;
            currentPhase = activation.Phase;

            try
            {
                if (UsesDynamicEffectTargets)
                    RefreshTargets();
                else
                    RestoreTargetSnapshot(snapshot);

                foreach (Spell4EffectsEntry effect in activation.Effects)
                {
                    try
                    {
                        OnEffectActivated(effect);
                    }
                    catch (Exception exception)
                    {
                        log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} effect {effect.Id} activation hook failed.");
                    }
                }

                ExecuteEffects(activation.Effects);
            }
            catch (Exception exception)
            {
                // Target selection and snapshot restoration failures consume this due activation so
                // one malformed row cannot retry forever or block later timeline work.
                log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} failed to prepare activation {activation.ActivationId}.");
            }
            finally
            {
                currentPhase = previousPhase;
            }
        }

        private bool TryInvokeEffectHandler(
            SpellEffectDelegate handler,
            Spell4EffectsEntry effect,
            SpellTargetInfo target,
            SpellTargetInfo.SpellTargetEffectInfo info)
        {
            bool succeeded = false;
            try
            {
                handler.Invoke(this, target.Entity, info);
                succeeded = true;
            }
            catch (Exception exception)
            {
                info.DropEffect = true;
                log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} effect {effect.Id} failed for target {target.Entity.Guid}.");
            }
            finally
            {
                try
                {
                    OnEffectAttempted(effect, target);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} effect {effect.Id} post-attempt hook failed for target {target.Entity.Guid}.");
                }
            }

            return succeeded;
        }

        private void RestoreTargetSnapshot(EffectActivationSnapshot snapshot)
        {
            targets.Clear();
            var casterMap = Caster.Map;
            if (!Caster.InWorld || casterMap == null)
                return;

            foreach (EffectTargetSnapshot target in snapshot.Targets)
            {
                if (!target.Entity.InWorld
                    || !ReferenceEquals(target.Entity.Map, casterMap))
                    continue;

                targets.Add(new SpellTargetInfo(target.Flags, target.Entity)
                {
                    TargetSelectionState = target.SelectionState
                });
            }
        }

        private void CleanupCompletedEffectSnapshots()
        {
            foreach (ulong activationId in effectActivationSnapshots.Keys.ToArray())
                if (!effectTimeline.HasPendingActivation(activationId))
                    effectActivationSnapshots.Remove(activationId);
        }

        private sealed record EffectActivationSnapshot(
            byte Phase,
            IReadOnlyList<EffectTargetSnapshot> Targets);

        private sealed record EffectTargetSnapshot(
            SpellEffectTargetFlags Flags,
            IUnitEntity Entity,
            TargetSelectionState SelectionState);

        private sealed record EffectExecutionPlan(
            Spell4EffectsEntry Effect,
            SpellEffectDelegate Handler,
            IReadOnlyList<SpellTargetInfo> Targets);

        public virtual bool IsMovingInterrupted()
        {
            // TODO: implement correctly
            return Parameters.SpellInfo.Entry.CastTime > 0;
        }

        /// <summary>
        /// Fail a spell before it begins casting and make it eligible for pending-spell cleanup.
        /// </summary>
        protected void FailCast(CastResult castResult)
        {
            if (castResult == CastResult.Ok)
                throw new ArgumentOutOfRangeException(nameof(castResult));

            try
            {
                SendSpellCastResult(castResult);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to publish cast failure for spell {Parameters.SpellInfo.Entry.Id}.");
            }
            finally
            {
                try
                {
                    events.CancelEvents();
                    RemoveAllEffects();
                }
                finally
                {
                    status = SpellStatus.Finishing;
                }
            }
        }

        protected void FailExecution(CastResult castResult)
        {
            try
            {
                try
                {
                    SendSpellCastResult(castResult);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to publish execution failure for spell {Parameters.SpellInfo.Entry.Id}.");
                }
            }
            finally
            {
                try
                {
                    try
                    {
                        if (IsCasting)
                            CancelCast(castResult);
                        else
                        {
                            try
                            {
                                SendSpellCancellation(castResult);
                            }
                            finally
                            {
                                events.CancelEvents();
                                RemoveAllEffects();
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        log.Error(exception, $"Failed to publish cancellation for spell {Parameters.SpellInfo.Entry.Id}.");
                    }
                }
                finally
                {
                    status = SpellStatus.Finishing;
                }
            }
        }

        private void SendSpellCancellation(CastResult castResult)
        {
            if (Caster is not IPlayer player || player.IsLoading)
                return;

            player.Session.EnqueueMessageEncrypted(new Server07F9
            {
                ServerUniqueId = CastingId,
                CastResult     = castResult,
                CancelCast     = true
            });
        }

        protected void SendSpellCastResult(CastResult castResult)
        {
            if (castResult == CastResult.Ok)
                return;

            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} failed to cast {castResult}.");

            if (Caster is IPlayer player && !player.IsLoading)
            {
                player.Session.EnqueueMessageEncrypted(new ServerSpellCastResult
                {
                    Spell4Id   = Parameters.SpellInfo.Entry.Id,
                    CastResult = castResult
                });
            }
        }

        private enum VitalCostMode
        {
            Skip,
            Validate,
            Consume
        }

        protected void SendSpellStart()
        {
            SendSpellStart(Parameters.PrimaryTargetId);
        }

        /// <summary>
        /// Publishes the spell start while selecting the unit used for its initial position block.
        /// </summary>
        protected void SendSpellStart(uint initialPositionUnitId)
        {
            var spellStart = new ServerSpellStart
            {
                CastingId              = CastingId,
                CasterId               = Caster.Guid,
                PrimaryTargetId        = Caster.Guid,
                Spell4Id               = Parameters.SpellInfo.Entry.Id,
                RootSpell4Id           = Parameters.RootSpellInfo?.Entry.Id ?? 0,
                ParentSpell4Id         = Parameters.ParentSpellInfo?.Entry.Id ?? 0,
                FieldPosition          = new Position(Caster.Position),
                Yaw                    = Caster.Rotation.X,
                UserInitiatedSpellCast = Parameters.UserInitiatedSpellCast,
                InitialPositionData    = new List<ServerSpellStart.InitialPosition>(),
                TelegraphPositionData  = new List<ServerSpellStart.TelegraphPosition>()
            };

            var unitsCasting = new List<IUnitEntity>();
            if (initialPositionUnitId > 0u)
            {
                IUnitEntity unit = Caster.GetVisible<IWorldEntity>(initialPositionUnitId) as IUnitEntity;
                if (unit != null)
                    unitsCasting.Add(unit);
            }
            else
                unitsCasting.Add(Caster);

            foreach (IUnitEntity unit in unitsCasting)
            {
                spellStart.InitialPositionData.Add(new ServerSpellStart.InitialPosition
                {
                    UnitId      = unit.Guid,
                    Position    = new Position(unit.Position),
                    TargetFlags = 3,
                    Yaw         = unit.Rotation.X
                });
            }

            foreach (IUnitEntity unit in unitsCasting)
            {
                foreach (ITelegraph telegraph in telegraphs.Where(IsTelegraphInCurrentPhase))
                {
                    spellStart.TelegraphPositionData.Add(new ServerSpellStart.TelegraphPosition
                    {
                        TelegraphId    = (ushort)telegraph.TelegraphDamage.Id,
                        AttachedUnitId = unit.Guid,
                        TargetFlags    = 3,
                        Position       = new Position(telegraph.Position),
                        Yaw            = telegraph.Rotation.X
                    });
                }
            }

            Caster.EnqueueToVisible(spellStart, true);
        }

        protected void SendSpellFinish()
        {
            if (status != SpellStatus.Finished)
                return;

            Caster.EnqueueToVisible(new ServerSpellFinish
            {
                ServerUniqueId = CastingId,
            }, true);
        }

        private void TrySendSpellGoEffect(
            IUnitEntity target,
            ISpellTargetEffectInfo effectInfo,
            bool handlerSucceeded)
        {
            foreach (ICombatLog combatLog in effectInfo.CombatLogs)
            {
                try
                {
                    Caster.EnqueueToVisible(new ServerCombatLog
                    {
                        CombatLog = combatLog
                    }, true);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} effect {effectInfo.Entry.Id} failed to publish a combat log for target {target.Guid}.");
                }
            }

            if (!handlerSucceeded
                || effectInfo.DropEffect
                || effectInfo.Entry.EffectType == SpellEffectType.Proxy)
                return;

            var packet = new Server07F8
            {
                CastingId     = CastingId,
                Spell4EffectId = effectInfo.Entry.Id,
                TargetId       = target.Guid
            };
            if (effectInfo.Damage != null)
                packet.DamageDescriptionData.Add(BuildDamageDescription(effectInfo.Damage));

            try
            {
                target.EnqueueToVisible(packet, true);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} effect {effectInfo.Entry.Id} activated for target {target.Guid} but its follow-up packet failed.");
            }
        }

        protected void SendSpellGo(byte phase)
        {
            List<ICombatLog> combatLogs = [];

            var serverSpellGo = new ServerSpellGo
            {
                ServerUniqueId     = CastingId,
                PrimaryDestination = new Position(Caster.Position),
                Phase              = phase == byte.MaxValue ? (sbyte)-1 : checked((sbyte)phase)
            };

            byte targetIndex = 0;
            foreach (ISpellTargetInfo targetInfo in targets
                .Where(t => t.Effects.Count > 0))
            {
                if (!targetInfo.Effects.Any(x => x.DropEffect == false))
                {
                    combatLogs.AddRange(targetInfo.Effects.SelectMany(i => i.CombatLogs));
                    continue;
                }

                var networkTargetInfo = new TargetInfo
                {
                    UnitId        = targetInfo.Entity.Guid,
                    Ndx           = targetIndex++,
                    TargetFlags   = checked((byte)targetInfo.Flags),
                    InstanceCount = 1,
                    CombatResult  = CombatResult.Hit
                };

                foreach (ISpellTargetEffectInfo targetEffectInfo in targetInfo.Effects)
                {
                    if (targetEffectInfo.DropEffect)
                    {
                        combatLogs.AddRange(targetEffectInfo.CombatLogs);
                        continue;
                    }

                    if (targetEffectInfo.Entry.EffectType == SpellEffectType.Proxy)
                        continue;

                    var networkTargetEffectInfo = new TargetInfo.EffectInfo
                    {
                        Spell4EffectId = targetEffectInfo.Entry.Id,
                        EffectUniqueId = targetEffectInfo.EffectId,
                        DelayTime      = targetEffectInfo.Entry.DelayTime,
                        TimeRemaining  = GetEffectTimeRemaining(targetEffectInfo.Entry)
                    };

                    if (targetEffectInfo.Damage != null)
                    {
                        networkTargetEffectInfo.InfoType = 1;
                        networkTargetEffectInfo.DamageDescriptionData = BuildDamageDescription(targetEffectInfo.Damage);
                    }

                    networkTargetInfo.EffectInfoData.Add(networkTargetEffectInfo);

                    combatLogs.AddRange(targetEffectInfo.CombatLogs);
                }

                serverSpellGo.TargetInfoData.Add(networkTargetInfo);
            }

            var unitsCasting = new List<IUnitEntity>
            {
                Caster
            };

            foreach (IUnitEntity unit in unitsCasting)
            {
                serverSpellGo.InitialPositionData.Add(new InitialPosition
                {
                    UnitId      = unit.Guid,
                    Position    = new Position(unit.Position),
                    TargetFlags = 3,
                    Yaw         = unit.Rotation.X
                });
            }

            foreach (IUnitEntity unit in unitsCasting)
            {
                foreach (ITelegraph telegraph in telegraphs.Where(IsTelegraphInCurrentPhase))
                {
                    serverSpellGo.TelegraphPositionData.Add(new TelegraphPosition
                    {
                        TelegraphId    = (ushort)telegraph.TelegraphDamage.Id,
                        AttachedUnitId = unit.Guid,
                        TargetFlags    = 3,
                        Position       = new Position(telegraph.Position),
                        Yaw            = telegraph.Rotation.X
                    });
                }
            }

            foreach (ICombatLog combatLog in combatLogs)
            {
                try
                {
                    Caster.EnqueueToVisible(new ServerCombatLog
                    {
                        CombatLog = combatLog
                    }, true);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Spell {Parameters.SpellInfo.Entry.Id} failed to publish an initial combat log.");
                }
            }

            Caster.EnqueueToVisible(serverSpellGo, true);

        }

        private int GetEffectTimeRemaining(Spell4EffectsEntry effect)
        {
            if (effect.DurationTime is > 0u and <= int.MaxValue)
                return (int)effect.DurationTime;

            bool hasOpenLifetime = effect.DurationTime == uint.MaxValue
                || effect.TickTime > 0u
                || ((SpellEffectFlags)effect.Flags & SpellEffectFlags.CancelOnly) != 0;
            uint rootDuration = Parameters.SpellInfo.Entry.SpellDuration;
            if (hasOpenLifetime && rootDuration is > 0u and <= int.MaxValue)
                return (int)rootDuration;

            return -1;
        }

        private static TargetInfo.EffectInfo.DamageDescription BuildDamageDescription(
            IDamageDescription damage)
        {
            return new TargetInfo.EffectInfo.DamageDescription
            {
                RawDamage          = damage.RawDamage,
                RawScaledDamage    = damage.RawScaledDamage,
                AbsorbedAmount     = damage.AbsorbedAmount,
                ShieldAbsorbAmount = damage.ShieldAbsorbAmount,
                AdjustedDamage     = damage.AdjustedDamage,
                OverkillAmount     = damage.OverkillAmount,
                KilledTarget       = damage.KilledTarget,
                CombatResult       = damage.CombatResult,
                DamageType         = damage.DamageType
            };
        }

        protected void SendRemoveBuff(uint unitId)
        {
            if (!Parameters.SpellInfo.BaseInfo.HasIcon)
                throw new InvalidOperationException();

            Caster.EnqueueToVisible(new ServerSpellBuffRemove
            {
                CastingId = CastingId,
                CasterId  = unitId
            }, true);
        }
    }
}
