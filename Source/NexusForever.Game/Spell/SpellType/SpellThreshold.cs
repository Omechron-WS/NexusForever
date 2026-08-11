using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    /// <summary>
    /// Shared, bounded lifecycle for build-16042 threshold-root spells.
    /// </summary>
    public abstract class SpellThreshold : Spell, IThresholdSpell
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        protected IReadOnlyList<Spell4ThresholdsEntry> ThresholdRows => thresholdRows;
        protected bool RootReady => rootReady;
        protected bool InputClosed => inputClosed;
        protected bool DispatchInProgress => dispatchInProgress;
        protected double ThresholdElapsedMilliseconds => thresholdElapsedMilliseconds;
        protected uint ThresholdWindowMilliseconds => castThresholdTime;

        private readonly CastMethod rootCastMethod;
        private IReadOnlyList<Spell4ThresholdsEntry> thresholdRows = [];
        private IReadOnlyList<ulong> cumulativeThresholdDurations = [];
        private readonly List<ISpell> thresholdChildren = [];

        private IBaseMap castMap;
        private ISpellInfo castSpellInfo;
        private ISpellInfo castRootSpellInfo;
        private IUnitEntity primaryTarget;
        private uint castPrimaryTargetId;
        private Position castPosition;
        private ushort castTaxiNode;
        private bool castWasUserInitiated;
        private bool castWasProcTriggered;
        private uint castThresholdTime;
        private double castRemainingSeconds;
        private double thresholdElapsedMilliseconds;
        private bool rootReady;
        private bool inputClosed;
        private bool dispatchInProgress;
        private bool thresholdStarted;
        private bool thresholdCleared;
        private bool cancellationInProgress;
        private bool configurationValid;
        private ISpellParameters dispatchingChildParameters;
        private uint dispatchingChildSpellId;

        protected SpellThreshold(
            IUnitEntity caster,
            ISpellParameters parameters,
            CastMethod rootCastMethod)
            : base(caster, parameters)
        {
            this.rootCastMethod = rootCastMethod;
        }

        public override void Cast()
        {
            if (Parameters.IsThresholdChild)
            {
                base.Cast();
                return;
            }

            if (status != SpellStatus.Initiating)
                throw new InvalidOperationException();

            configurationValid = TryCreateThresholdSnapshot(
                rootCastMethod,
                out thresholdRows,
                out cumulativeThresholdDurations);
            if (!configurationValid || !TryCaptureCastContext())
            {
                FailCast(CastResult.SpellBad);
                return;
            }

            CastResult result = CheckCast();
            if (result != CastResult.Ok)
            {
                FailCast(result);
                return;
            }

            if (Caster is IPlayer player && Parameters.SpellInfo.GlobalCooldown != null)
                player.SpellManager.SetGlobalSpellCooldown(
                    Parameters.SpellInfo.Entry.GlobalCooldownEnum,
                    Parameters.SpellInfo.GlobalCooldown.CooldownTime / 1000d);

            if (Caster is not IPlayer)
                InitialiseTelegraphs();

            SendSpellStart();

            castRemainingSeconds = Parameters.SpellInfo.Entry.CastTime / 1000d;
            status = SpellStatus.Casting;
            log.Trace($"Spell {castSpellInfo.Entry.Id} has started {rootCastMethod} casting.");
        }

        public override void Update(double lastTick)
        {
            if (!double.IsFinite(lastTick) || lastTick < 0d)
                return;

            if (Parameters.IsThresholdChild)
            {
                base.Update(lastTick);
                return;
            }

            double remaining = lastTick;
            if (!rootReady && status == SpellStatus.Casting)
            {
                if (remaining < castRemainingSeconds)
                {
                    castRemainingSeconds -= remaining;
                    base.Update(remaining);
                    return;
                }

                double beforeReady = castRemainingSeconds;
                castRemainingSeconds = 0d;
                base.Update(beforeReady);
                remaining -= beforeReady;

                if (status != SpellStatus.Casting)
                    return;

                OpenThresholdWindow();
            }

            if (remaining > 0d || rootReady)
                base.Update(remaining);

            if (!rootReady || inputClosed || status != SpellStatus.Waiting)
                return;

            AddThresholdElapsed(remaining);
            AdvanceThreshold(remaining);
        }

        public bool TryHandleThresholdInput(bool buttonPressed)
        {
            if (Parameters.IsThresholdChild)
                return false;

            HandleThresholdInput(buttonPressed);
            return true;
        }

        public bool IsValidThresholdChild(ISpell child)
        {
            if (Parameters.IsThresholdChild
                || child == null
                || !child.Parameters.IsThresholdChild
                || !ReferenceEquals(child.Parameters.ThresholdParent, this)
                || !ReferenceEquals(child.Caster, Caster)
                || !ReferenceEquals(child.Parameters.ParentSpellInfo, castSpellInfo)
                || !ReferenceEquals(child.Parameters.RootSpellInfo, castRootSpellInfo)
                || child.Parameters.ThresholdValue == 0)
                return false;

            bool isBeingDispatched = dispatchInProgress
                && ReferenceEquals(child.Parameters, dispatchingChildParameters)
                && child.Parameters.SpellInfo?.Entry.Id == dispatchingChildSpellId;
            bool isTracked = thresholdChildren.Any(candidate => ReferenceEquals(candidate, child));
            if (!isBeingDispatched && !isTracked)
                return false;

            int rowIndex = child.Parameters.ThresholdValue - 1;
            return rowIndex >= 0
                && rowIndex < thresholdRows.Count
                && thresholdRows[rowIndex].OrderIndex == (uint)rowIndex
                && thresholdRows[rowIndex].Spell4IdToCast == child.Parameters.SpellInfo?.Entry.Id;
        }

        public override void CancelCast(CastResult result)
        {
            if (Parameters.IsThresholdChild)
            {
                base.CancelCast(result);
                return;
            }

            if (status == SpellStatus.Finished || cancellationInProgress)
                return;

            if (!IsCasting)
            {
                Finish();
                return;
            }

            cancellationInProgress = true;
            inputClosed = true;
            try
            {
                StopThresholdChildren(result);
                base.CancelCast(result);
            }
            finally
            {
                TrySendThresholdClear();
                cancellationInProgress = false;
            }
        }

        public override void Finish()
        {
            if (Parameters.IsThresholdChild)
            {
                base.Finish();
                return;
            }

            if (status == SpellStatus.Finished)
                return;

            inputClosed = true;
            StopThresholdChildren(CastResult.SpellCancelled);
            try
            {
                base.Finish();
            }
            finally
            {
                TrySendThresholdClear();
            }
        }

        public override void Dispose()
        {
            if (Parameters.IsThresholdChild)
            {
                base.Dispose();
                return;
            }

            inputClosed = true;
            try
            {
                StopThresholdChildren(CastResult.SpellCancelled);
                base.Dispose();
            }
            finally
            {
                TrySendThresholdClear();
            }
        }

        protected override bool IsCastingInternal()
        {
            if (Parameters.IsThresholdChild)
                return base.IsCastingInternal();

            return status is SpellStatus.Casting or SpellStatus.Executing or SpellStatus.Waiting;
        }

        protected override bool CanFinish()
        {
            if (Parameters.IsThresholdChild)
                return base.CanFinish();

            if (thresholdChildren.Any(child => !child.IsFinished))
                return false;

            return base.CanFinish();
        }

        /// <summary>
        /// Handle a root-specific button transition.
        /// </summary>
        protected abstract void HandleThresholdInput(bool buttonPressed);

        /// <summary>
        /// Advance root-specific threshold timing after the root is ready.
        /// </summary>
        protected abstract void AdvanceThreshold(double elapsedSeconds);

        /// <summary>
        /// Invoked after the parent executes once and its threshold window is open.
        /// </summary>
        protected virtual void OnThresholdReady()
        {
        }

        /// <summary>
        /// Dispatch one exact threshold child and, for ChargeRelease, atomically consume all reached row costs.
        /// </summary>
        protected bool TryActivateThreshold(
            int rowIndex,
            bool consumeCumulativeThresholdCosts,
            out CastResult failure)
        {
            failure = CastResult.SpellBad;
            if (dispatchInProgress
                || (inputClosed && rootCastMethod == CastMethod.RapidTap)
                || rowIndex < 0
                || rowIndex >= thresholdRows.Count
                || !HasValidDispatchContext())
                return false;

            IReadOnlyList<Spell4ThresholdsEntry> costRows = consumeCumulativeThresholdCosts
                ? thresholdRows.Take(rowIndex + 1).ToArray()
                : [];
            if (consumeCumulativeThresholdCosts && Caster is IPlayer)
            {
                failure = SpellVitalPolicy.CheckCosts(Caster, costRows);
                if (failure != CastResult.Ok)
                    return false;
            }

            Spell4ThresholdsEntry row = thresholdRows[rowIndex];
            var childParameters = new SpellParameters
            {
                ParentSpellInfo        = castSpellInfo,
                RootSpellInfo          = castRootSpellInfo,
                UserInitiatedSpellCast = castWasUserInitiated,
                IsProcTriggered        = castWasProcTriggered,
                IsThresholdChild       = true,
                ThresholdValue         = checked((byte)(row.OrderIndex + 1u)),
                ThresholdParent        = this,
                PrimaryTargetId        = castPrimaryTargetId,
                Position               = castPosition == null
                    ? null
                    : new Position(castPosition.Vector),
                TaxiNode               = castTaxiNode
            };

            dispatchInProgress = true;
            dispatchingChildParameters = childParameters;
            dispatchingChildSpellId = row.Spell4IdToCast;
            ISpell child = null;
            try
            {
                child = CastThresholdChild(row.Spell4IdToCast, childParameters);
                if (child == null
                    || child.IsFinished
                    || child.IsFinishing
                    || !ReferenceEquals(child.Caster, Caster)
                    || !ReferenceEquals(child.Parameters, childParameters)
                    || child.Parameters.SpellInfo?.Entry.Id != row.Spell4IdToCast)
                {
                    StopThresholdChild(child, CastResult.SpellBad);
                    return false;
                }

                thresholdChildren.Add(child);

                if (consumeCumulativeThresholdCosts && Caster is IPlayer)
                {
                    failure = SpellVitalPolicy.TryConsumeCosts(Caster, costRows);
                    if (failure != CastResult.Ok)
                    {
                        StopThresholdChild(child, failure);
                        return false;
                    }
                }

                failure = CastResult.Ok;
                PublishThresholdValue(childParameters.ThresholdValue);
                return true;
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Spell {castSpellInfo.Entry.Id} failed to start threshold child {row.Spell4IdToCast}.");
                StopThresholdChild(child, CastResult.SpellBad);
                return false;
            }
            finally
            {
                dispatchingChildParameters = null;
                dispatchingChildSpellId = 0u;
                dispatchInProgress = false;
            }
        }

        /// <summary>
        /// Close input without cancelling already accepted child or parent effect lifetimes.
        /// </summary>
        protected void CloseThresholdWindow()
        {
            if (inputClosed)
                return;

            BeginThresholdClose();
            PublishThresholdClear();
        }

        /// <summary>
        /// Commit terminal threshold state before invoking external release work.
        /// </summary>
        protected void BeginThresholdClose()
        {
            inputClosed = true;
            if (status != SpellStatus.Finished)
                status = SpellStatus.Finishing;
        }

        /// <summary>
        /// Publish the terminal threshold clear at most once.
        /// </summary>
        protected void PublishThresholdClear()
        {
            TrySendThresholdClear();
        }

        /// <summary>
        /// Fail the threshold transaction and clean every accepted child.
        /// </summary>
        protected void FailThreshold(CastResult result)
        {
            if (result == CastResult.Ok)
                result = CastResult.SpellBad;

            inputClosed = true;
            StopThresholdChildren(result);
            try
            {
                FailExecution(result);
            }
            finally
            {
                TrySendThresholdClear();
            }
        }

        protected ulong GetCumulativeThresholdDuration(int rowIndex)
        {
            return cumulativeThresholdDurations[rowIndex];
        }

        /// <summary>
        /// Publish a monotonic one-based threshold selection at most once.
        /// </summary>
        protected void PublishThresholdValue(byte value)
        {
            TrySendThresholdUpdate(value);
        }

        private void OpenThresholdWindow()
        {
            rootReady = true;
            try
            {
                Execute();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Spell {castSpellInfo.Entry.Id} failed while opening its threshold window.");
                FailThreshold(CastResult.SpellBad);
                return;
            }

            if (status != SpellStatus.Executing)
                return;

            status = SpellStatus.Waiting;
            thresholdStarted = true;
            TrySendThresholdStart();
            OnThresholdReady();
        }

        private bool TryCreateThresholdSnapshot(
            CastMethod expectedCastMethod,
            out IReadOnlyList<Spell4ThresholdsEntry> rows,
            out IReadOnlyList<ulong> cumulativeDurations)
        {
            rows = [];
            cumulativeDurations = [];

            try
            {
                if ((CastMethod)Parameters.SpellInfo.BaseInfo.Entry.CastMethod != expectedCastMethod
                    || expectedCastMethod is not (CastMethod.RapidTap or CastMethod.ChargeRelease)
                    || Parameters.ParentSpellInfo != null
                    || !ReferenceEquals(Parameters.RootSpellInfo, Parameters.SpellInfo)
                    || Parameters.SpellInfo.Entry.ThresholdTime == 0u)
                    return false;

                Spell4ThresholdsEntry[] allThresholdRows = GetThresholdEntries()?.ToArray();
                if (allThresholdRows == null)
                    return false;

                Spell4ThresholdsEntry[] snapshot = allThresholdRows
                    .Where(row => row != null
                        && row.Spell4IdParent == Parameters.SpellInfo.Entry.Id)
                    .Select(CloneThresholdRow)
                    .OrderBy(row => row.OrderIndex)
                    .ThenBy(row => row.Id)
                    .ToArray();
                if (snapshot.Length == 0 || snapshot.Length > byte.MaxValue)
                    return false;

                if (snapshot.Any(row => row.Id == 0u)
                    || snapshot.Select(row => row.Id).Distinct().Count() != snapshot.Length)
                    return false;

                var cumulative = new List<ulong>(snapshot.Length);
                ulong totalDuration = 0u;
                for (int i = 0; i < snapshot.Length; i++)
                {
                    Spell4ThresholdsEntry row = snapshot[i];
                    if (row.OrderIndex != (uint)i
                        || row.Spell4IdToCast == 0u
                        || row.Spell4IdToCast == Parameters.SpellInfo.Entry.Id)
                        return false;

                    Spell4Entry childEntry = GetSpellEntry(row.Spell4IdToCast);
                    if (childEntry == null || childEntry.TierIndex > byte.MaxValue)
                        return false;

                    Spell4BaseEntry childBaseEntry = GetSpellBaseEntry(childEntry.Spell4BaseIdBaseSpell);
                    CastMethod childCastMethod = childBaseEntry == null
                        ? unchecked((CastMethod)uint.MaxValue)
                        : (CastMethod)childBaseEntry.CastMethod;
                    if (childCastMethod is not (CastMethod.Normal or CastMethod.RapidTap or CastMethod.ChargeRelease))
                        return false;

                    if (allThresholdRows.Any(candidate => candidate != null
                        && candidate.Spell4IdParent == row.Spell4IdToCast))
                        return false;

                    totalDuration += row.ThresholdDuration;
                    cumulative.Add(totalDuration);
                }

                if (expectedCastMethod == CastMethod.RapidTap)
                {
                    if (snapshot.Any(SpellVitalPolicy.HasCost))
                        return false;
                }
                else if (totalDuration > Parameters.SpellInfo.Entry.ThresholdTime
                    || !SpellVitalPolicy.HasSupportedCosts(snapshot))
                    return false;

                rows = snapshot;
                cumulativeDurations = cumulative;
                return true;
            }
            catch
            {
                rows = [];
                cumulativeDurations = [];
                return false;
            }
        }

        private bool TryCaptureCastContext()
        {
            try
            {
                castSpellInfo = Parameters.SpellInfo;
                castRootSpellInfo = Parameters.RootSpellInfo;
                if (castSpellInfo == null
                    || !ReferenceEquals(castRootSpellInfo, castSpellInfo))
                    return false;

                if (!Caster.IsAlive || !Caster.InWorld || Caster.Map == null)
                    return false;

                if (!IsFinite(Caster.Position)
                    || Parameters.Position != null && !IsFinite(Parameters.Position.Vector))
                    return false;

                castMap = Caster.Map;
                castPrimaryTargetId = Parameters.PrimaryTargetId;
                castPosition = Parameters.Position == null
                    ? null
                    : new Position(Parameters.Position.Vector);
                castTaxiNode = Parameters.TaxiNode;
                castWasUserInitiated = Parameters.UserInitiatedSpellCast;
                castWasProcTriggered = Parameters.IsProcTriggered;
                castThresholdTime = castSpellInfo.Entry.ThresholdTime;

                if (castPrimaryTargetId == 0u)
                    return true;

                primaryTarget = castPrimaryTargetId == Caster.Guid
                    ? Caster
                    : Caster.GetVisible<IWorldEntity>(castPrimaryTargetId) as IUnitEntity;
                return primaryTarget != null
                    && primaryTarget.InWorld
                    && ReferenceEquals(primaryTarget.Map, castMap);
            }
            catch
            {
                castMap = null;
                castSpellInfo = null;
                castRootSpellInfo = null;
                primaryTarget = null;
                return false;
            }
        }

        private bool HasValidDispatchContext()
        {
            try
            {
                if (!Caster.IsAlive
                    || !Caster.InWorld
                    || !ReferenceEquals(Caster.Map, castMap)
                    || !IsFinite(Caster.Position))
                    return false;

                if (castPrimaryTargetId == 0u)
                    return true;

                IUnitEntity currentTarget = castPrimaryTargetId == Caster.Guid
                    ? Caster
                    : Caster.GetVisible<IWorldEntity>(castPrimaryTargetId) as IUnitEntity;
                return ReferenceEquals(currentTarget, primaryTarget)
                    && currentTarget.InWorld
                    && ReferenceEquals(currentTarget.Map, castMap);
            }
            catch
            {
                return false;
            }
        }

        private void AddThresholdElapsed(double elapsedSeconds)
        {
            double elapsedMilliseconds = elapsedSeconds * 1000d;
            thresholdElapsedMilliseconds = double.IsFinite(elapsedMilliseconds)
                && elapsedMilliseconds <= double.MaxValue - thresholdElapsedMilliseconds
                    ? thresholdElapsedMilliseconds + elapsedMilliseconds
                    : double.MaxValue;
        }

        private void StopThresholdChildren(CastResult result)
        {
            foreach (ISpell child in thresholdChildren.ToArray())
                StopThresholdChild(child, result);
        }

        private static void StopThresholdChild(ISpell child, CastResult result)
        {
            if (child == null || child.IsFinished)
                return;

            try
            {
                if (child.IsCasting)
                    child.CancelCast(result);
                else
                    child.Finish();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to stop threshold child {child.CastingId}.");
                try
                {
                    child.Finish();
                }
                catch (Exception cleanupException)
                {
                    log.Error(cleanupException, $"Failed to force-finish threshold child {child.CastingId}.");
                }
            }
        }

        private void TrySendThresholdStart()
        {
            if (Caster is not IPlayer player || player.IsLoading)
                return;

            try
            {
                player.Session.EnqueueMessageEncrypted(new Server0816
                {
                    Spell4Id       = castSpellInfo.Entry.Id,
                    RootSpell4Id   = castRootSpellInfo.Entry.Id,
                    ParentSpell4Id = 0u,
                    CastingId      = CastingId
                });
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Spell {castSpellInfo.Entry.Id} failed to publish its threshold start.");
            }
        }

        private byte lastThresholdUpdate;

        private void TrySendThresholdUpdate(byte value)
        {
            if (value == 0 || value <= lastThresholdUpdate)
                return;

            lastThresholdUpdate = value;
            if (Caster is not IPlayer player || player.IsLoading)
                return;

            try
            {
                player.Session.EnqueueMessageEncrypted(new Server0817
                {
                    Spell4Id = castSpellInfo.Entry.Id,
                    Unknown0 = value
                });
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Spell {castSpellInfo.Entry.Id} failed to publish threshold value {value}.");
            }
        }

        private void TrySendThresholdClear()
        {
            if (!thresholdStarted || thresholdCleared)
                return;

            thresholdCleared = true;
            if (Caster is not IPlayer player || player.IsLoading)
                return;

            try
            {
                player.Session.EnqueueMessageEncrypted(new Server0814
                {
                    Spell4Id = castSpellInfo.Entry.Id,
                    Unknown0 = false
                });
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Spell {castSpellInfo.Entry.Id} failed to publish its threshold clear.");
            }
        }

        private static bool IsFinite(System.Numerics.Vector3 value)
        {
            return float.IsFinite(value.X)
                && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
        }

        private static Spell4ThresholdsEntry CloneThresholdRow(Spell4ThresholdsEntry row)
        {
            return new Spell4ThresholdsEntry
            {
                Id                     = row.Id,
                Spell4IdParent         = row.Spell4IdParent,
                Spell4IdToCast         = row.Spell4IdToCast,
                OrderIndex             = row.OrderIndex,
                ThresholdDuration      = row.ThresholdDuration,
                VitalEnumCostType00    = row.VitalEnumCostType00,
                VitalEnumCostType01    = row.VitalEnumCostType01,
                VitalCostValue00       = row.VitalCostValue00,
                VitalCostValue01       = row.VitalCostValue01,
                LocalizedTextIdTooltip = row.LocalizedTextIdTooltip,
                IconReplacement        = row.IconReplacement,
                VisualEffectId         = row.VisualEffectId
            };
        }

        /// <summary>
        /// Return threshold rows used to construct the immutable root snapshot.
        /// </summary>
        protected virtual IEnumerable<Spell4ThresholdsEntry> GetThresholdEntries()
        {
            return GameTableManager.Instance.Spell4Thresholds?.Entries;
        }

        /// <summary>
        /// Resolve a threshold child spell row while constructing the immutable root snapshot.
        /// </summary>
        protected virtual Spell4Entry GetSpellEntry(uint spell4Id)
        {
            return GameTableManager.Instance.Spell4?.GetEntry(spell4Id);
        }

        /// <summary>
        /// Resolve a threshold child base row while constructing the immutable root snapshot.
        /// </summary>
        protected virtual Spell4BaseEntry GetSpellBaseEntry(uint spell4BaseId)
        {
            return GameTableManager.Instance.Spell4Base?.GetEntry(spell4BaseId);
        }

        /// <summary>
        /// Start one exact child. UnitEntity owns the returned child lifecycle.
        /// </summary>
        protected virtual ISpell CastThresholdChild(uint spell4Id, ISpellParameters parameters)
        {
            return Caster.CastSpellTracked(spell4Id, parameters);
        }
    }
}
