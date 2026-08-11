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

        private IScriptCollection scriptCollection;
        private bool executionCommitted;
        private bool? unsupportedThresholdVitalCost;

        protected byte currentPhase = 255;

        public Spell(IUnitEntity caster, ISpellParameters parameters)
        {
            Caster     = caster;
            Parameters = parameters;
            CastingId  = GlobalSpellManager.Instance.NextCastingId;
            status     = SpellStatus.Initiating;

            parameters.RootSpellInfo ??= parameters.SpellInfo;

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
            if (status == SpellStatus.Executing && !events.HasPendingEvent)
                return true;

            if (status == SpellStatus.Finishing && !events.HasPendingEvent)
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
                : Caster.GetVisible<IUnitEntity>(Parameters.PrimaryTargetId);
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
                if (Caster is IPlayer player && !player.IsLoading)
                {
                    player.Session.EnqueueMessageEncrypted(new Server07F9
                    {
                        ServerUniqueId = CastingId,
                        CastResult     = result,
                        CancelCast     = true
                    });
                }
            }
            finally
            {
                events.CancelEvents();
                RemoveAllEffects();
                status = SpellStatus.Executing;
            }

            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} cast was cancelled.");
        }

        /// <summary>
        /// Force-end the spell and all its effects.
        /// </summary>
        public virtual void Finish()
        {
            if (status is SpellStatus.Finished or SpellStatus.Finishing)
                return;

            events.CancelEvents();
            RemoveAllEffects();
            status = SpellStatus.Finishing;
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

        private void RemoveAllEffects()
        {
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

            SelectTargets();
            ExecuteEffects();

            SendSpellGo();
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
                IUnitEntity primaryTargetEntity = Caster.GetVisible<IUnitEntity>(Parameters.PrimaryTargetId);
                if (primaryTargetEntity != null)
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Target, primaryTargetEntity));
            }

            if (Caster is IPlayer)
                InitialiseTelegraphs();

            foreach (ITelegraph telegraph in telegraphs)
            {
                foreach (IUnitEntity entity in telegraph.GetTargets())
                    targets.Add(new SpellTargetInfo(SpellEffectTargetFlags.Telegraph, entity));
            }
        }

        protected void ExecuteEffects()
        {
            foreach (Spell4EffectsEntry spell4EffectsEntry in Parameters.SpellInfo.Effects)
            {
                // select targets for effect
                List<ISpellTargetInfo> effectTargets = targets
                    .Where(t => (t.Flags & (SpellEffectTargetFlags)spell4EffectsEntry.TargetFlags) != 0)
                    .ToList();

                SpellEffectDelegate handler = GlobalSpellManager.Instance.GetEffectHandler((SpellEffectType)spell4EffectsEntry.EffectType);
                if (handler == null)
                    log.Warn($"Unhandled spell effect {(SpellEffectType)spell4EffectsEntry.EffectType}");
                else
                {
                    uint effectId = GlobalSpellManager.Instance.NextEffectId;
                    foreach (SpellTargetInfo effectTarget in effectTargets)
                    {
                        var info = new SpellTargetInfo.SpellTargetEffectInfo(effectId, spell4EffectsEntry);
                        effectTarget.Effects.Add(info);

                        // TODO: if there is an unhandled exception in the handler, there will be an infinite loop on Execute()
                        handler.Invoke(this, effectTarget.Entity, info);
                    }
                }
            }
        }

        public bool IsMovingInterrupted()
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

        private void FailExecution(CastResult castResult)
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
                            events.CancelEvents();
                            RemoveAllEffects();
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
            if (Parameters.PrimaryTargetId > 0)
                unitsCasting.Add(Caster.GetVisible<IUnitEntity>(Parameters.PrimaryTargetId));
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
                foreach (ITelegraph telegraph in telegraphs)
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

        protected void SendSpellGo()
        {
            List<ICombatLog> combatLogs = [];

            var serverSpellGo = new ServerSpellGo
            {
                ServerUniqueId     = CastingId,
                PrimaryDestination = new Position(Caster.Position),
                Phase              = -1
            };

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
                    TargetFlags   = 1,
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
                        TimeRemaining  = -1
                    };

                    if (targetEffectInfo.Damage != null)
                    {
                        networkTargetEffectInfo.InfoType = 1;
                        networkTargetEffectInfo.DamageDescriptionData = new TargetInfo.EffectInfo.DamageDescription
                        {
                            RawDamage          = targetEffectInfo.Damage.RawDamage,
                            RawScaledDamage    = targetEffectInfo.Damage.RawScaledDamage,
                            AbsorbedAmount     = targetEffectInfo.Damage.AbsorbedAmount,
                            ShieldAbsorbAmount = targetEffectInfo.Damage.ShieldAbsorbAmount,
                            AdjustedDamage     = targetEffectInfo.Damage.AdjustedDamage,
                            OverkillAmount     = targetEffectInfo.Damage.OverkillAmount,
                            KilledTarget       = targetEffectInfo.Damage.KilledTarget,
                            CombatResult       = targetEffectInfo.Damage.CombatResult,
                            DamageType         = targetEffectInfo.Damage.DamageType
                        };
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
                foreach (ITelegraph telegraph in telegraphs)
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
                Caster.EnqueueToVisible(new ServerCombatLog
                {
                    CombatLog = combatLog
                }, true);
            }

            Caster.EnqueueToVisible(serverSpellGo, true);

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
