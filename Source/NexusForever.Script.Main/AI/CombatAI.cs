using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement.Command.Position;
using NexusForever.Game.Abstract.Entity.Movement.Generator;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Entity;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Main.AI
{
    /// <summary>
    /// Minimal, reactive combat profiles for the build-16042 Ravenok pilot and provisional Scrab mapping.
    /// </summary>
    [ScriptFilterCreatureId(33932u, 24054u)]
    public class CombatAI : IOwnedScript<ICreatureEntity>, IUnitScript
    {
        private const uint RavenokCreatureId = 33932u;
        private const uint ScrabCreatureId = 24054u;
        private const string LoggerCategory = "NexusForever.Script.Main.AI.CombatAI";

        private static readonly CombatProfile ravenokProfile = new(
            RavenokCreatureId,
            65812u,
            1.5d,
            5f,
            15f,
            0.25d,
            1f,
            0.2f,
            7f,
            15f);

        // Provisional Scrab mapping and 1.5-second policy: build 16042 identifies the attack but not retail cadence.
        private static readonly CombatProfile scrabProfile = new(
            ScrabCreatureId,
            65785u,
            1.5d,
            10f,
            15f,
            0.25d,
            1f,
            0.2f,
            7f,
            15f);

        private readonly IDirectMovementGenerator directMovementGenerator;
        private readonly ILogger log;

        private ICreatureEntity owner;
        private IBaseMap attachedMap;
        private IBaseMap pendingAttachMap;
        private CombatProfile profile;
        private CombatAIState state = CombatAIState.Detached;

        private Vector3 homePosition;
        private Vector3 homeRotation;
        private Vector3 lastChaseTarget;

        private double attackRemaining;
        private double chaseReplanRemaining;

        private bool targetSelectionDirty;
        private bool aiMovementActive;
        private bool aiMovementIssued;
        private bool movementCleanupPending;
        private bool targetCleanupPending;
        private bool threatCleanupPending;
        private bool rotationCleanupPending;
        private uint? facingTargetGuid;

        public CombatAI(
            ILoggerFactory loggerFactory,
            IDirectMovementGenerator directMovementGenerator)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            ArgumentNullException.ThrowIfNull(directMovementGenerator);

            log = loggerFactory.CreateLogger(LoggerCategory);
            ArgumentNullException.ThrowIfNull(log);
            this.directMovementGenerator = directMovementGenerator;
        }

        public void OnLoad(ICreatureEntity owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            this.owner = owner;
            profile = GetProfile(owner.CreatureId);
            state = profile != null ? CombatAIState.Detached : CombatAIState.Disabled;

            // Source hot reload creates a replacement script for the existing collection, but map lifecycle
            // callbacks are not replayed. Stop any inherited AI path before attaching to the live map.
            if (profile != null && owner.InWorld && owner.Map != null)
            {
                homePosition = owner.LeashPosition;
                if (owner.Spline == null)
                    movementCleanupPending = true;
                TryAttachToMap(owner.Map);
            }
        }

        public void OnUnload()
        {
            state = CombatAIState.Detached;
            QueueOwnedStateCleanup(clearThreat: false, restoreRotation: attachedMap != null);
            ProcessPendingCleanup();

            attachedMap = null;
            pendingAttachMap = null;
            profile = null;
            owner = null;
            aiMovementActive = false;
            aiMovementIssued = false;
            ClearPendingCleanup();
        }

        public void OnAddToMap(IBaseMap map)
        {
            TryAttachToMap(map);
        }

        public void OnRemoveFromMap(IBaseMap map)
        {
            if (attachedMap != null && map != null && !ReferenceEquals(attachedMap, map))
                return;

            state = CombatAIState.Detached;
            QueueOwnedStateCleanup(clearThreat: true, restoreRotation: attachedMap != null);
            ProcessPendingCleanup();

            attachedMap = null;
            pendingAttachMap = null;
            targetSelectionDirty = false;
            aiMovementActive = false;
            facingTargetGuid = null;
        }

        public void Update(double lastTick)
        {
            if (owner == null)
                return;

            if (state is CombatAIState.Dead or CombatAIState.Disabled)
            {
                ProcessPendingCleanup();
                return;
            }

            if (state == CombatAIState.Detached)
            {
                if (pendingAttachMap != null)
                    TryAttachToMap(pendingAttachMap);
                else
                    ProcessPendingCleanup();
                return;
            }

            if (!IsProfiledOwner() || owner.Spline != null)
            {
                DisableActivePilot();
                return;
            }

            if (!owner.InWorld
                || owner.Map == null
                || !ReferenceEquals(owner.Map, attachedMap))
            {
                state = CombatAIState.Detached;
                QueueOwnedStateCleanup(clearThreat: false, restoreRotation: attachedMap != null);
                ProcessPendingCleanup();
                attachedMap = null;
                pendingAttachMap = null;
                targetSelectionDirty = false;
                aiMovementActive = false;
                facingTargetGuid = null;
                return;
            }

            // Death and detachment are safety transitions and must preempt an invalid world delta.
            if (!owner.IsAlive)
            {
                EnterDeadState();
                return;
            }

            // A dead instance is never resurrected in place. Persistent respawn creates a fresh entity and script.
            if (state == CombatAIState.Dead)
                return;

            if (!double.IsFinite(lastTick) || lastTick <= 0d)
            {
                // Movement updates after scripts in the same owner tick. Freeze an AI-owned spline before the
                // invalid delta can enter the spline accumulator, but preserve combat timers and state.
                if (aiMovementIssued)
                    RequestStopAiMovement();

                return;
            }

            if (movementCleanupPending)
            {
                ProcessPendingCleanup();
                if (movementCleanupPending)
                    return;
            }

            if (state == CombatAIState.Evading)
            {
                UpdateEvade();
                return;
            }

            if (state == CombatAIState.Idle
                && !targetSelectionDirty
                && !owner.ThreatManager.IsThreatened)
                return;

            IUnitEntity target;
            IHostileEntity hostile;
            try
            {
                if (!TrySelectTarget(out target, out hostile))
                {
                    HandleMissingTarget();
                    return;
                }
            }
            catch (Exception exception)
            {
                log.LogError(
                    exception,
                    "Creature {CreatureId} ({Guid}) failed to validate its threat targets.",
                    owner.CreatureId,
                    owner.Guid);
                targetSelectionDirty = true;
                HandleMissingTarget();
                return;
            }

            bool targetChanged = owner.TargetGuid != target.Guid;
            if (targetChanged)
            {
                owner.SetTarget(target, hostile.Threat);
                facingTargetGuid = null;
            }

            bool enteredCombat = state == CombatAIState.Idle;
            if (enteredCombat)
            {
                state = CombatAIState.Engaged;
                attackRemaining = profile.AttackInterval;
                chaseReplanRemaining = 0d;
            }

            UpdateEngaged(target, lastTick, enteredCombat);
        }

        public void OnThreatAddTarget(IHostileEntity hostile)
        {
            // Threat creation establishes its reciprocal relationship after this callback returns. Mutating the
            // threat list here can leave an orphan reciprocal entry, so selection and pruning happen in Update.
            targetSelectionDirty = true;
        }

        public void OnThreatRemoveTarget(IHostileEntity hostile)
        {
            targetSelectionDirty = true;
        }

        public void OnThreatChange(IHostileEntity hostile)
        {
            targetSelectionDirty = true;
        }

        public void OnPositionEntityCommandFinalise(IPositionCommand command)
        {
            if (!aiMovementIssued)
                return;

            aiMovementActive = false;
            chaseReplanRemaining = 0d;
        }

        private bool TryAttachToMap(IBaseMap map)
        {
            pendingAttachMap = map;
            ProcessPendingCleanup();
            if (HasPendingCleanup)
            {
                state = CombatAIState.Detached;
                return false;
            }

            if (!IsProfiledOwner()
                || map == null
                || !ReferenceEquals(owner.Map, map)
                || owner.Spline != null)
            {
                pendingAttachMap = null;
                DisableActivePilot();
                return false;
            }

            Vector3 position = owner.MovementManager.GetPosition();
            Vector3 rotation = owner.MovementManager.GetRotation();
            Vector3 leashPosition = owner.LeashPosition;
            if (!IsFinite(position) || !IsFinite(rotation) || !IsFinite(leashPosition))
            {
                DisableActivePilot();
                return false;
            }

            attachedMap = map;
            pendingAttachMap = null;
            homePosition = leashPosition;
            homeRotation = rotation;
            lastChaseTarget = position;
            attackRemaining = profile.AttackInterval;
            chaseReplanRemaining = 0d;
            targetSelectionDirty = true;
            aiMovementActive = false;
            aiMovementIssued = false;
            ClearPendingCleanup();
            facingTargetGuid = null;
            bool awayFromHome = !TryGetDistance(position, homePosition, out float homeDistance)
                || homeDistance > profile.HomeArrivalTolerance;
            state = CombatAIState.Idle;
            if (awayFromHome)
            {
                try
                {
                    if (!owner.ThreatManager.IsThreatened)
                        state = CombatAIState.Evading;
                }
                catch (Exception exception)
                {
                    log.LogError(
                        exception,
                        "Creature {CreatureId} ({Guid}) failed to inspect its threat state while attaching.",
                        owner.CreatureId,
                        owner.Guid);
                }
            }

            if (!owner.IsAlive)
                EnterDeadState();

            return state != CombatAIState.Disabled;
        }

        private void HandleMissingTarget()
        {
            if (state == CombatAIState.Engaged)
            {
                BeginEvade();
                return;
            }

            if (!TryIsOutsideHomeTolerance(out bool outsideHomeTolerance))
            {
                targetSelectionDirty = true;
                return;
            }

            if (outsideHomeTolerance)
            {
                BeginEvade();
                return;
            }

            targetCleanupPending = true;
            ProcessPendingCleanup();
        }

        private bool TryIsOutsideHomeTolerance(out bool outsideHomeTolerance)
        {
            outsideHomeTolerance = false;
            try
            {
                Vector3 position = owner.MovementManager.GetPosition();
                outsideHomeTolerance = !IsFinite(position)
                    || !TryGetDistance(position, homePosition, out float homeDistance)
                    || homeDistance > profile.HomeArrivalTolerance;
                return true;
            }
            catch (Exception exception)
            {
                log.LogError(
                    exception,
                    "Creature {CreatureId} ({Guid}) failed to inspect its distance from its leash.",
                    owner.CreatureId,
                    owner.Guid);
                return false;
            }
        }

        private bool TrySelectTarget(out IUnitEntity target, out IHostileEntity selectedHostile)
        {
            target = null;
            selectedHostile = null;
            targetSelectionDirty = false;

            var rejectedIds = new HashSet<uint>();
            while (true)
            {
                IHostileEntity hostile = owner.ThreatManager.GetTopHostile();
                if (hostile == null)
                    return false;

                if (!rejectedIds.Add(hostile.HatedUnitId))
                    return false;

                // Never remove a replacement entry through a stale callback/reference.
                if (!ReferenceEquals(owner.ThreatManager.GetHostile(hostile.HatedUnitId), hostile))
                {
                    targetSelectionDirty = true;
                    return false;
                }

                IUnitEntity candidate = owner.GetVisible<IUnitEntity>(hostile.HatedUnitId);
                if (IsValidTarget(hostile, candidate))
                {
                    target = candidate;
                    selectedHostile = hostile;
                    targetSelectionDirty = false;
                    return true;
                }

                owner.ThreatManager.RemoveHostile(hostile.HatedUnitId);
                // Removal callbacks only mark selection dirty. This loop observes the completed removal and
                // selects the next deterministic entry without mutating from inside the callback itself.
                targetSelectionDirty = false;
            }
        }

        private bool IsValidTarget(IHostileEntity hostile, IUnitEntity candidate)
        {
            return hostile.Threat > 0u
                && candidate != null
                && candidate.Guid == hostile.HatedUnitId
                && candidate.Guid != owner.Guid
                && candidate.IsAlive
                && candidate.InWorld
                && ReferenceEquals(candidate.Map, attachedMap)
                && ReferenceEquals(candidate.Map, owner.Map)
                && owner.CanAttack(candidate);
        }

        private void UpdateEngaged(IUnitEntity target, double lastTick, bool enteredCombat)
        {
            Vector3 currentPosition = owner.MovementManager.GetPosition();
            Vector3 targetPosition = target.MovementManager.GetPosition();
            if (!IsFinite(currentPosition)
                || !IsFinite(targetPosition)
                || !TryGetDistance(currentPosition, homePosition, out float homeDistance)
                || homeDistance > profile.LeashRange)
            {
                BeginEvade();
                return;
            }

            if (!enteredCombat)
                attackRemaining = AdvanceCountdown(attackRemaining, lastTick);
            chaseReplanRemaining = AdvanceCountdown(chaseReplanRemaining, lastTick);

            if (!TryGetDistance(currentPosition, targetPosition, out float targetDistance))
            {
                BeginEvade();
                return;
            }

            if (targetDistance > profile.AttackRange)
            {
                bool targetMoved = !TryGetDistance(lastChaseTarget, targetPosition, out float targetMovement)
                    || targetMovement >= profile.ChaseTargetMovement;
                if (!aiMovementActive || chaseReplanRemaining == 0d || targetMoved)
                {
                    chaseReplanRemaining = profile.ChaseReplanInterval;
                    lastChaseTarget = targetPosition;
                    if (!TryGetMovementSpeed(profile.ChaseSpeedMultiplier, out float chaseSpeed))
                    {
                        BeginEvade();
                        return;
                    }

                    LaunchMovement(currentPosition, targetPosition, chaseSpeed);
                }

                return;
            }

            if (aiMovementIssued && !RequestStopAiMovement())
                return;

            if (facingTargetGuid != target.Guid)
            {
                owner.MovementManager.SetRotationFaceUnit(target.Guid);
                facingTargetGuid = target.Guid;
            }

            if (enteredCombat || attackRemaining > 0d)
                return;

            if (owner.GetActiveSpell(spell => spell.IsCasting) != null)
                return;

            // Reset before admission so a null result or isolated exception cannot create a frame-rate retry loop.
            attackRemaining = profile.AttackInterval;
            try
            {
                owner.CastSpellTracked(profile.AutoAttackSpellId, new CombatSpellParameters
                {
                    PrimaryTargetId = target.Guid
                });
            }
            catch (Exception exception)
            {
                log.LogError(
                    exception,
                    "Creature {CreatureId} ({Guid}) failed to cast profiled auto-attack {SpellId}.",
                    owner.CreatureId,
                    owner.Guid,
                    profile.AutoAttackSpellId);
            }
        }

        private void BeginEvade()
        {
            // Set the state before threat callbacks fire so damage during return can never reacquire a target.
            state = CombatAIState.Evading;
            attackRemaining = profile.AttackInterval;
            chaseReplanRemaining = 0d;
            facingTargetGuid = null;

            targetCleanupPending = true;
            threatCleanupPending = true;
            if (aiMovementIssued)
            {
                movementCleanupPending = true;
                aiMovementActive = false;
            }

            ProcessPendingCleanup();
            UpdateEvade();
        }

        private void UpdateEvade()
        {
            targetCleanupPending = true;
            threatCleanupPending = true;
            ProcessPendingCleanup();
            targetSelectionDirty = false;

            if (targetCleanupPending || threatCleanupPending || movementCleanupPending)
                return;

            Vector3 currentPosition = owner.MovementManager.GetPosition();
            if (!IsFinite(currentPosition))
            {
                RecoverAtHome();
                currentPosition = homePosition;
            }

            if (!TryGetDistance(currentPosition, homePosition, out float homeDistance))
                return;

            if (homeDistance <= profile.HomeArrivalTolerance)
            {
                CompleteEvade();
                return;
            }

            if (!aiMovementActive
                && TryGetMovementSpeed(profile.ReturnSpeedMultiplier, out float returnSpeed))
                LaunchMovement(currentPosition, homePosition, returnSpeed);
        }

        private void CompleteEvade()
        {
            // Clear once more before Idle so threat received during the return cannot survive the reset boundary.
            targetCleanupPending = true;
            threatCleanupPending = true;
            if (aiMovementIssued)
            {
                movementCleanupPending = true;
                aiMovementActive = false;
            }
            rotationCleanupPending = true;

            ProcessPendingCleanup();
            targetSelectionDirty = false;

            if (HasPendingCleanup)
                return;

            facingTargetGuid = null;

            if (!owner.IsAlive)
            {
                EnterDeadState();
                return;
            }

            if (owner.Health < owner.MaxHealth)
                owner.ModifyHealth(owner.MaxHealth - owner.Health, DamageType.Heal, owner);

            if (!owner.IsAlive)
            {
                EnterDeadState();
                return;
            }

            attackRemaining = profile.AttackInterval;
            chaseReplanRemaining = 0d;
            state = CombatAIState.Idle;
        }

        private void EnterDeadState()
        {
            bool enteringState = state != CombatAIState.Dead;

            state = CombatAIState.Dead;
            targetSelectionDirty = false;
            facingTargetGuid = null;

            targetCleanupPending = true;
            if (aiMovementIssued)
            {
                movementCleanupPending = true;
                aiMovementActive = false;
            }
            if (enteringState && attachedMap != null)
                rotationCleanupPending = true;

            ProcessPendingCleanup();
        }

        private void DisableActivePilot()
        {
            bool ownedState = attachedMap != null
                && (state is CombatAIState.Idle or CombatAIState.Engaged or CombatAIState.Evading
                    || aiMovementIssued);

            state = CombatAIState.Disabled;
            pendingAttachMap = null;
            targetSelectionDirty = false;
            facingTargetGuid = null;

            if (ownedState)
                QueueOwnedStateCleanup(clearThreat: true, restoreRotation: true);
            ProcessPendingCleanup();
        }

        private void LaunchMovement(Vector3 begin, Vector3 final, float speed)
        {
            if (!IsFinite(begin)
                || !IsFinite(final)
                || !float.IsFinite(speed)
                || speed <= 0f
                || movementCleanupPending
                || owner.Map == null
                || !ReferenceEquals(owner.Map, attachedMap))
                return;

            directMovementGenerator.Begin = begin;
            directMovementGenerator.Final = final;
            directMovementGenerator.Map = attachedMap;

            // LaunchGenerator is a no-op without server control, while the AI still records the request as
            // issued. Preserve that ownership contract without calculating a path that would not be launched.
            if (!owner.MovementManager.ServerControl)
            {
                aiMovementActive = true;
                aiMovementIssued = true;
                facingTargetGuid = null;
                return;
            }

            List<Vector3> nodes;
            try
            {
                nodes = directMovementGenerator.CalculatePath();
            }
            catch (Exception exception)
            {
                log.LogError(
                    exception,
                    "Creature {CreatureId} ({Guid}) failed to calculate its AI movement path.",
                    owner.CreatureId,
                    owner.Guid);
                return;
            }

            if (nodes == null || nodes.Count < 2 || nodes.Any(node => !IsFinite(node)))
            {
                log.LogError(
                    "Creature {CreatureId} ({Guid}) generated an invalid AI movement path.",
                    owner.CreatureId,
                    owner.Guid);
                return;
            }

            aiMovementIssued = true;
            movementCleanupPending = true;
            aiMovementActive = false;
            facingTargetGuid = null;

            try
            {
                owner.MovementManager.LaunchSpline(nodes, SplineType.Linear, SplineMode.OneShot, speed);
            }
            catch (Exception exception)
            {
                log.LogError(
                    exception,
                    "Creature {CreatureId} ({Guid}) failed to launch its AI movement path.",
                    owner.CreatureId,
                    owner.Guid);
                ProcessMovementCleanup();
                return;
            }

            movementCleanupPending = false;
            aiMovementActive = true;
        }

        private bool RequestStopAiMovement()
        {
            if (!aiMovementIssued)
                return true;

            movementCleanupPending = true;
            aiMovementActive = false;
            ProcessMovementCleanup();
            return !movementCleanupPending;
        }

        private void QueueOwnedStateCleanup(bool clearThreat, bool restoreRotation)
        {
            if (owner == null)
                return;

            targetCleanupPending = true;
            threatCleanupPending |= clearThreat;
            rotationCleanupPending |= restoreRotation && IsFinite(homeRotation);
            if (aiMovementIssued)
            {
                movementCleanupPending = true;
                aiMovementActive = false;
            }
        }

        private void ProcessPendingCleanup()
        {
            if (owner == null)
                return;

            if (targetCleanupPending)
            {
                try
                {
                    if (owner.TargetGuid != null)
                        owner.SetTarget((IWorldEntity)null);
                    targetCleanupPending = false;
                }
                catch (Exception exception)
                {
                    LogCleanupFailure(exception, "clear its target");
                }
            }

            if (threatCleanupPending)
            {
                try
                {
                    if (owner.ThreatManager.IsThreatened)
                        owner.ThreatManager.ClearThreatList();
                    threatCleanupPending = owner.ThreatManager.IsThreatened;
                }
                catch (Exception exception)
                {
                    LogCleanupFailure(exception, "clear its threat list");
                }
            }

            ProcessMovementCleanup();

            if (rotationCleanupPending)
            {
                try
                {
                    owner.MovementManager.SetRotation(homeRotation, true);
                    rotationCleanupPending = false;
                }
                catch (Exception exception)
                {
                    LogCleanupFailure(exception, "restore its leash rotation");
                }
            }
        }

        private void ProcessMovementCleanup()
        {
            if (!movementCleanupPending)
                return;

            bool succeeded = true;
            Vector3 position = homePosition;
            try
            {
                Vector3 currentPosition = owner.MovementManager.GetPosition();
                if (IsFinite(currentPosition))
                    position = currentPosition;
            }
            catch (Exception exception)
            {
                LogCleanupFailure(exception, "read its current movement position");
            }

            if (!IsFinite(position))
                succeeded = false;
            else
            {
                try
                {
                    owner.MovementManager.SetPosition(position, true);
                }
                catch (Exception exception)
                {
                    succeeded = false;
                    LogCleanupFailure(exception, "freeze its AI movement");
                }
            }

            try
            {
                owner.MovementManager.SetMoveDefaults(false);
            }
            catch (Exception exception)
            {
                succeeded = false;
                LogCleanupFailure(exception, "reset its move command");
            }

            try
            {
                owner.MovementManager.SetStateDefault();
            }
            catch (Exception exception)
            {
                succeeded = false;
                LogCleanupFailure(exception, "reset its movement state");
            }

            aiMovementActive = false;
            if (!succeeded)
                return;

            movementCleanupPending = false;
            aiMovementIssued = false;
        }

        private void ClearPendingCleanup()
        {
            movementCleanupPending = false;
            targetCleanupPending = false;
            threatCleanupPending = false;
            rotationCleanupPending = false;
        }

        private bool HasPendingCleanup => movementCleanupPending
            || targetCleanupPending
            || threatCleanupPending
            || rotationCleanupPending;

        private void LogCleanupFailure(Exception exception, string operation)
        {
            log.LogError(
                exception,
                "Creature {CreatureId} ({Guid}) failed to {Operation} during AI cleanup.",
                owner.CreatureId,
                owner.Guid,
                operation);
        }

        private void RecoverAtHome()
        {
            if (!IsFinite(homePosition))
                return;

            owner.MovementManager.SetPosition(homePosition, false);
            owner.MovementManager.SetMoveDefaults(false);
            owner.MovementManager.SetStateDefault();

            aiMovementActive = false;
            aiMovementIssued = false;
            movementCleanupPending = false;
        }

        private static CombatProfile GetProfile(uint creatureId)
        {
            return creatureId switch
            {
                RavenokCreatureId => ravenokProfile,
                ScrabCreatureId => scrabProfile,
                _ => null
            };
        }

        private bool IsProfiledOwner()
        {
            return owner != null
                && profile != null
                && owner.CreatureId == profile.CreatureId;
        }

        private bool TryGetMovementSpeed(float profileMultiplier, out float speed)
        {
            float ownerMultiplier = owner.GetPropertyValue(Property.MoveSpeedMultiplier);
            speed = ownerMultiplier * profileMultiplier;
            return float.IsFinite(ownerMultiplier)
                && ownerMultiplier > 0f
                && float.IsFinite(speed)
                && speed > 0f;
        }

        private static double AdvanceCountdown(double remaining, double elapsed)
        {
            double updated = remaining - elapsed;
            return updated <= 0.000000001d ? 0d : updated;
        }

        private static bool TryGetDistance(Vector3 first, Vector3 second, out float distance)
        {
            distance = Vector3.Distance(first, second);
            return float.IsFinite(distance);
        }

        private static bool IsFinite(Vector3 vector)
        {
            return float.IsFinite(vector.X)
                && float.IsFinite(vector.Y)
                && float.IsFinite(vector.Z);
        }

        private enum CombatAIState
        {
            Detached,
            Disabled,
            Idle,
            Engaged,
            Evading,
            Dead
        }

        private sealed class CombatProfile
        {
            public uint CreatureId { get; }
            public uint AutoAttackSpellId { get; }
            public double AttackInterval { get; }
            public float AttackRange { get; }
            public float LeashRange { get; }
            public double ChaseReplanInterval { get; }
            public float ChaseTargetMovement { get; }
            public float HomeArrivalTolerance { get; }
            public float ChaseSpeedMultiplier { get; }
            public float ReturnSpeedMultiplier { get; }

            public CombatProfile(
                uint creatureId,
                uint autoAttackSpellId,
                double attackInterval,
                float attackRange,
                float leashRange,
                double chaseReplanInterval,
                float chaseTargetMovement,
                float homeArrivalTolerance,
                float chaseSpeedMultiplier,
                float returnSpeedMultiplier)
            {
                CreatureId = creatureId;
                AutoAttackSpellId = autoAttackSpellId;
                AttackInterval = attackInterval;
                AttackRange = attackRange;
                LeashRange = leashRange;
                ChaseReplanInterval = chaseReplanInterval;
                ChaseTargetMovement = chaseTargetMovement;
                HomeArrivalTolerance = homeArrivalTolerance;
                ChaseSpeedMultiplier = chaseSpeedMultiplier;
                ReturnSpeedMultiplier = returnSpeedMultiplier;
            }
        }

        private sealed class CombatSpellParameters : ISpellParameters
        {
            public ICharacterSpell CharacterSpell { get; set; }
            public ISpellInfo SpellInfo { get; set; }
            public ISpellInfo ParentSpellInfo { get; set; }
            public ISpellInfo RootSpellInfo { get; set; }
            public bool UserInitiatedSpellCast { get; set; }
            public bool IsProcTriggered { get; set; }
            public uint CastTimeOverride { get; set; }
            public uint PrimaryTargetId { get; set; }
            public Position Position { get; set; }
            public ushort TaxiNode { get; set; }
            public IClientSideInteraction ClientSideInteraction { get; set; }
        }
    }
}
