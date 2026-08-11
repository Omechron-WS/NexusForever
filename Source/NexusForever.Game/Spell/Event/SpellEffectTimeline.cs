using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Spell.Event
{
    /// <summary>
    /// Schedules build-data spell effects independently from cast and channel pulse events.
    /// </summary>
    internal sealed class SpellEffectTimeline
    {
        internal const int MaximumCallbacksPerUpdate = 128;

        public bool HasPendingEffect => schedules.Count != 0;

        private readonly uint rootDuration;
        private readonly List<ScheduledEffect> schedules = [];

        private double elapsedMilliseconds;
        private ulong nextSequence;

        public SpellEffectTimeline(uint rootDuration)
        {
            this.rootDuration = rootDuration;
        }

        /// <summary>
        /// Registers one effect at the timeline's current position.
        /// </summary>
        public SpellEffectRegistrationResult Register(
            Spell4EffectsEntry entry,
            byte phase,
            ulong activationId)
        {
            ArgumentNullException.ThrowIfNull(entry);

            bool cancelOnly = ((SpellEffectFlags)entry.Flags & SpellEffectFlags.CancelOnly) != 0;
            bool finiteEffectDuration = entry.DurationTime is > 0u and < uint.MaxValue;
            bool openEffectDuration = entry.DurationTime == uint.MaxValue;
            bool requiresLifetime = finiteEffectDuration
                || openEffectDuration
                || entry.TickTime > 0u
                || cancelOnly;

            double? lifetimeMilliseconds = null;
            if (finiteEffectDuration)
                lifetimeMilliseconds = entry.DurationTime;
            else if (requiresLifetime && rootDuration is > 0u and < uint.MaxValue)
                lifetimeMilliseconds = rootDuration;
            else if (requiresLifetime && !cancelOnly)
                return SpellEffectRegistrationResult.UnsupportedOpenEnded;

            double activationStart = elapsedMilliseconds + entry.DelayTime;
            double? expirationTime = requiresLifetime && lifetimeMilliseconds.HasValue
                ? activationStart + lifetimeMilliseconds.Value
                : null;
            // Build 16042 does not state whether a periodic row invokes at activation time. The
            // compatibility assumption is that its first invocation follows one complete tick.
            double? nextActivation = entry.TickTime > 0u
                ? activationStart + entry.TickTime
                : activationStart;

            // A finite ticking effect has floor(Duration / TickTime) periodic invocations.
            if (entry.TickTime > 0u
                && expirationTime.HasValue
                && nextActivation.Value > expirationTime.Value)
                nextActivation = null;

            schedules.Add(new ScheduledEffect
            {
                Entry              = entry,
                Phase              = phase,
                ActivationId       = activationId,
                Sequence           = nextSequence++,
                NextActivationTime = nextActivation,
                TickTime           = entry.TickTime,
                ExpirationTime     = expirationTime,
                HoldsOpen          = requiresLifetime && !expirationTime.HasValue
            });

            return SpellEffectRegistrationResult.Registered;
        }

        /// <summary>
        /// Returns whether work remains for one independently captured execution snapshot.
        /// </summary>
        public bool HasPendingActivation(ulong activationId)
        {
            return schedules.Any(schedule => schedule.ActivationId == activationId);
        }

        /// <summary>
        /// Returns whether any overlapping execution still owns the supplied effect row.
        /// </summary>
        public bool HasPendingEntry(Spell4EffectsEntry entry)
        {
            return schedules.Any(schedule => ReferenceEquals(schedule.Entry, entry));
        }

        /// <summary>
        /// Advances the timeline and returns due operations in deterministic chronological order.
        /// </summary>
        /// <remarks>
        /// The per-update activation bound prevents a corrupt world tick or a very small tick interval from
        /// monopolising the world thread. The full elapsed duration is retained and due work drains later.
        /// </remarks>
        public IReadOnlyList<SpellEffectTimelineEvent> Advance(double elapsedSeconds)
        {
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d)
                return [];

            double advanceMilliseconds = elapsedSeconds * 1000d;
            elapsedMilliseconds = double.IsFinite(advanceMilliseconds)
                && advanceMilliseconds <= double.MaxValue - elapsedMilliseconds
                    ? elapsedMilliseconds + advanceMilliseconds
                    : double.MaxValue;

            List<SpellEffectTimelineEvent> dueEvents = [];
            int callbackCount = 0;

            while (schedules.Count != 0)
            {
                ScheduledEffect nextActivation = schedules
                    .Where(schedule => schedule.NextActivationTime.HasValue
                        && schedule.NextActivationTime.Value <= elapsedMilliseconds)
                    .OrderBy(schedule => schedule.NextActivationTime.Value)
                    .ThenBy(schedule => schedule.Sequence)
                    .FirstOrDefault();
                ScheduledEffect nextExpiration = schedules
                    .Where(schedule => schedule.ExpirationTime.HasValue
                        && schedule.ExpirationTime.Value <= elapsedMilliseconds)
                    .OrderBy(schedule => schedule.ExpirationTime.Value)
                    .ThenBy(schedule => schedule.Sequence)
                    .FirstOrDefault();

                if (nextActivation == null && nextExpiration == null)
                    break;

                if (callbackCount >= MaximumCallbacksPerUpdate)
                    break;

                if (nextExpiration != null
                    && (nextActivation == null
                        || nextExpiration.ExpirationTime.Value < nextActivation.NextActivationTime.Value))
                {
                    callbackCount += ExpireAt(
                        nextExpiration.ExpirationTime.Value,
                        MaximumCallbacksPerUpdate - callbackCount,
                        dueEvents);
                    continue;
                }

                double activationTime = nextActivation.NextActivationTime.Value;
                byte phase = nextActivation.Phase;
                ulong activationId = nextActivation.ActivationId;
                int remainingCapacity = MaximumCallbacksPerUpdate - callbackCount;
                List<ScheduledEffect> activationBatch = schedules
                    .Where(schedule => schedule.NextActivationTime == activationTime
                        && schedule.Phase == phase
                        && schedule.ActivationId == activationId)
                    .OrderBy(schedule => schedule.Sequence)
                    .Take(remainingCapacity)
                    .ToList();

                foreach (ScheduledEffect schedule in activationBatch)
                    AdvanceSchedule(schedule);

                callbackCount += activationBatch.Count;
                dueEvents.Add(new SpellEffectActivation(
                    activationTime,
                    phase,
                    activationId,
                    activationBatch.Select(schedule => schedule.Entry).ToArray()));
            }

            return dueEvents;
        }

        /// <summary>
        /// Cancels every pending effect and permits the owning spell to finish.
        /// </summary>
        public void Cancel()
        {
            schedules.Clear();
        }

        private void AdvanceSchedule(ScheduledEffect schedule)
        {
            if (schedule.TickTime > 0u)
            {
                double candidate = schedule.NextActivationTime.Value + schedule.TickTime;
                schedule.NextActivationTime = schedule.ExpirationTime.HasValue
                    && candidate > schedule.ExpirationTime.Value
                        ? null
                        : candidate;
            }
            else
                schedule.NextActivationTime = null;

            if (!schedule.NextActivationTime.HasValue
                && !schedule.ExpirationTime.HasValue
                && !schedule.HoldsOpen)
                schedules.Remove(schedule);
        }

        private int ExpireAt(
            double expirationTime,
            int maximumExpirations,
            ICollection<SpellEffectTimelineEvent> dueEvents)
        {
            ScheduledEffect[] expiringSchedules = schedules
                .Where(candidate => candidate.ExpirationTime == expirationTime)
                .OrderBy(candidate => candidate.Sequence)
                .Take(maximumExpirations)
                .ToArray();
            foreach (ScheduledEffect schedule in expiringSchedules)
            {
                schedules.Remove(schedule);
                dueEvents.Add(new SpellEffectExpiration(
                    expirationTime,
                    schedule.Phase,
                    schedule.ActivationId,
                    schedule.Entry));
            }

            return expiringSchedules.Length;
        }

        private sealed class ScheduledEffect
        {
            public required Spell4EffectsEntry Entry { get; init; }
            public required byte Phase { get; init; }
            public required ulong ActivationId { get; init; }
            public required ulong Sequence { get; init; }
            public double? NextActivationTime { get; set; }
            public required uint TickTime { get; init; }
            public double? ExpirationTime { get; init; }
            public bool HoldsOpen { get; init; }
        }
    }

    internal enum SpellEffectRegistrationResult
    {
        Registered,
        UnsupportedOpenEnded
    }

    internal abstract record SpellEffectTimelineEvent(double TimeMilliseconds);

    internal sealed record SpellEffectActivation(
        double TimeMilliseconds,
        byte Phase,
        ulong ActivationId,
        IReadOnlyList<Spell4EffectsEntry> Effects)
        : SpellEffectTimelineEvent(TimeMilliseconds);

    internal sealed record SpellEffectExpiration(
        double TimeMilliseconds,
        byte Phase,
        ulong ActivationId,
        Spell4EffectsEntry Effect)
        : SpellEffectTimelineEvent(TimeMilliseconds);
}
