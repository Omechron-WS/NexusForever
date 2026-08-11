using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NLog;

namespace NexusForever.Game.Spell.SpellType
{
    /// <summary>
    /// Spell type whose terminal result is driven by a build-16042 client-side interaction.
    /// </summary>
    /// <remarks>
    /// Packet dispatch and spell updates are serialised by the world loop. The terminal state additionally rejects
    /// duplicate or replayed client results; it does not make the spell event collection generally thread-safe.
    /// </remarks>
    [SpellType(CastMethod.ClientSideInteraction)]
    public class SpellClientSideInteraction : Spell, IClientSideInteractionSpell
    {
        private const double DefaultResultTimeoutMilliseconds = 60_000d;
        private const double MaximumResultTimeoutMilliseconds = 300_000d;
        private const double ResultGraceMilliseconds = 5_000d;

        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public bool IsInteractionPending => Volatile.Read(ref terminalState) == 0 && IsCasting;
        public bool RequiresClientResult => Parameters.ClientSideInteraction?.Entry != null;

        private int terminalState;
        private bool castReady;
        private bool successPending;

        public SpellClientSideInteraction(IUnitEntity caster, ISpellParameters parameters)
            : base(caster, parameters)
        {
        }

        public override void Cast()
        {
            if (status != SpellStatus.Initiating)
                throw new InvalidOperationException();

            if (Parameters.ClientSideInteraction == null || Caster is not IPlayer)
            {
                Interlocked.CompareExchange(ref terminalState, 2, 0);
                FailCast(CastResult.SpellBad);
                return;
            }

            CastResult result;
            try
            {
                result = CheckCast();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to validate client-side interaction spell {Parameters.SpellInfo.Entry.Id}.");
                Interlocked.CompareExchange(ref terminalState, 2, 0);
                TriggerFailureCallback();
                FailCast(CastResult.SpellBad);
                return;
            }

            if (result != CastResult.Ok)
            {
                Interlocked.CompareExchange(ref terminalState, 2, 0);
                TriggerFailureCallback();
                FailCast(result);
                return;
            }

            if (Caster is IPlayer player && Parameters.SpellInfo.GlobalCooldown != null)
            {
                try
                {
                    player.SpellManager.SetGlobalSpellCooldown(Parameters.SpellInfo.GlobalCooldown.CooldownTime / 1000d);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to commit global cooldown for client-side interaction spell {Parameters.SpellInfo.Entry.Id}.");
                    Interlocked.CompareExchange(ref terminalState, 2, 0);
                    TriggerFailureCallback();
                    FailCast(CastResult.SpellBad);
                    return;
                }
            }

            try
            {
                SendInteractionStart();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to publish client-side interaction start for spell {Parameters.SpellInfo.Entry.Id}.");
                Interlocked.CompareExchange(ref terminalState, 2, 0);
                TriggerFailureCallback();
                FailCast(CastResult.SpellBad);
                return;
            }

            double castTimeSeconds = GetEffectiveCastTimeMilliseconds() / 1000d;
            castReady = castTimeSeconds <= 0d;

            if (RequiresClientResult)
            {
                if (!castReady)
                    events.EnqueueEvent(new SpellEvent(castTimeSeconds, OnCastReady));

                events.EnqueueEvent(new SpellEvent(GetResultTimeoutSeconds(), () => FailClientInteraction()));
            }
            else
            {
                events.EnqueueEvent(new SpellEvent(
                    castTimeSeconds,
                    OnProxyCastReady));
            }

            status = SpellStatus.Casting;
            log.Trace($"Spell {Parameters.SpellInfo.Entry.Id} has started CSI casting.");
        }

        /// <inheritdoc />
        public bool SucceedClientInteraction()
        {
            if (Volatile.Read(ref terminalState) != 0)
                return false;

            if (!castReady)
            {
                if (successPending)
                    return false;

                successPending = true;
                return true;
            }

            return CompleteSuccessfulInteraction();
        }

        private bool CompleteSuccessfulInteraction()
        {
            if (Interlocked.CompareExchange(ref terminalState, 1, 0) != 0)
                return false;

            events.CancelEvents();
            if (!IsInteractionValid())
            {
                TriggerFailureCallback();
                CancelClaimedInteraction(CastResult.ClientSideInteractionFail);
                return true;
            }

            try
            {
                Execute();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Client-side interaction spell {Parameters.SpellInfo.Entry.Id} failed during execution.");
                TriggerFailureCallback();
                FailExecution(CastResult.SpellBad);
                return true;
            }

            if (IsFinishing || IsFinished)
            {
                TriggerFailureCallback();
                return true;
            }

            TriggerSuccessCallback();

            return true;
        }

        private void OnCastReady()
        {
            castReady = true;
            if (successPending)
                CompleteSuccessfulInteraction();
        }

        private void OnProxyCastReady()
        {
            castReady = true;
            CompleteSuccessfulInteraction();
        }

        /// <inheritdoc />
        public bool FailClientInteraction()
        {
            if (Interlocked.CompareExchange(ref terminalState, 2, 0) != 0)
                return false;

            TriggerFailureCallback();
            CancelClaimedInteraction(CastResult.ClientSideInteractionFail);
            return true;
        }

        /// <inheritdoc />
        public bool CancelClientInteraction()
        {
            if (Interlocked.CompareExchange(ref terminalState, 3, 0) != 0)
                return false;

            CancelClaimedInteraction(CastResult.ClientSideInteractionFail);
            return true;
        }

        public override void CancelCast(CastResult result)
        {
            if (!IsCasting)
                return;

            if (Interlocked.CompareExchange(ref terminalState, 2, 0) == 0)
                TriggerFailureCallback();

            base.CancelCast(result);
        }

        public override void Finish()
        {
            if (Interlocked.CompareExchange(ref terminalState, 2, 0) == 0)
                TriggerFailureCallback();

            base.Finish();
        }

        /// <inheritdoc />
        public override bool IsMovingInterrupted()
        {
            return GetEffectiveCastTimeMilliseconds() > 0d;
        }

        private void SendInteractionStart()
        {
            if (Parameters.ClientSideInteraction.Entry != null)
            {
                SendSpellStart(0u);
                return;
            }

            IPlayer player = (IPlayer)Caster;
            player.Session.EnqueueMessageEncrypted(new ServerSpellStartClientInteraction
            {
                ClientUniqueId = Parameters.ClientSideInteraction.ClientUniqueId,
                CastingId      = CastingId,
                CasterId       = Parameters.PrimaryTargetId != 0u ? Parameters.PrimaryTargetId : Caster.Guid
            });
        }

        private double GetResultTimeoutSeconds()
        {
            uint duration = Parameters.ClientSideInteraction.Entry?.Duration ?? 0u;
            double durationMilliseconds = duration is 0u or uint.MaxValue
                ? DefaultResultTimeoutMilliseconds
                : Math.Min(duration, MaximumResultTimeoutMilliseconds);
            return (GetEffectiveCastTimeMilliseconds() + durationMilliseconds + ResultGraceMilliseconds) / 1000d;
        }

        private double GetEffectiveCastTimeMilliseconds()
        {
            return Parameters.CastTimeOverride > 0u
                ? Parameters.CastTimeOverride
                : Parameters.SpellInfo.Entry.CastTime;
        }

        private bool IsInteractionValid()
        {
            try
            {
                return Parameters.ClientSideInteraction.IsValid();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to validate client-side interaction spell {Parameters.SpellInfo.Entry.Id}.");
                return false;
            }
        }

        private void TriggerSuccessCallback()
        {
            try
            {
                if (!Parameters.ClientSideInteraction.CompleteSuccess())
                    log.Warn($"Client-side interaction callback was already completed for spell {Parameters.SpellInfo.Entry.Id}.");
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Client-side interaction success callback failed for spell {Parameters.SpellInfo.Entry.Id}.");
            }
        }

        private void TriggerFailureCallback()
        {
            try
            {
                Parameters.ClientSideInteraction?.TriggerFail();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Client-side interaction failure callback failed for spell {Parameters.SpellInfo.Entry.Id}.");
            }
        }

        private void CancelClaimedInteraction(CastResult result)
        {
            try
            {
                if (IsCasting)
                    base.CancelCast(result);
                else
                    Finish();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to cancel client-side interaction spell {Parameters.SpellInfo.Entry.Id}.");
                base.Finish();
            }
        }
    }
}
