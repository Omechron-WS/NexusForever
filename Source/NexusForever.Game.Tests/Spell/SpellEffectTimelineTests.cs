using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Tests.Spell
{
    public sealed class SpellEffectTimelineTests
    {
        [Fact]
        public void DelayedOneShot_ActivatesAtDelayAndExpiresAfterRelativeDuration()
        {
            var timeline = new SpellEffectTimeline(0u);
            var effect = new Spell4EffectsEntry
            {
                Id           = 1u,
                DelayTime    = 5_000u,
                DurationTime = 1_000u
            };

            Assert.Equal(SpellEffectRegistrationResult.Registered, timeline.Register(effect, byte.MaxValue, 1u));
            Assert.Empty(timeline.Advance(4.999d));

            SpellEffectActivation activation = Assert.IsType<SpellEffectActivation>(
                Assert.Single(timeline.Advance(0.001d)));
            Assert.Equal(5_000d, activation.TimeMilliseconds);
            Assert.Same(effect, Assert.Single(activation.Effects));
            Assert.True(timeline.HasPendingEffect);

            Assert.Empty(timeline.Advance(0.999d));
            SpellEffectExpiration expiration = Assert.IsType<SpellEffectExpiration>(
                Assert.Single(timeline.Advance(0.001d)));
            Assert.Equal(6_000d, expiration.TimeMilliseconds);
            Assert.Same(effect, expiration.Effect);
            Assert.False(timeline.HasPendingEffect);
        }

        [Fact]
        public void PeriodicEffect_FirstTickIsDelayPlusTickAndCountIsDurationDividedByTick()
        {
            var timeline = new SpellEffectTimeline(0u);
            var effect = new Spell4EffectsEntry
            {
                Id           = 2u,
                DelayTime    = 5_000u,
                TickTime     = 1_000u,
                DurationTime = 3_000u
            };

            Assert.Equal(SpellEffectRegistrationResult.Registered, timeline.Register(effect, (byte)4u, 1u));
            IReadOnlyList<SpellEffectTimelineEvent> events = timeline.Advance(8d);

            SpellEffectActivation[] activations = events.OfType<SpellEffectActivation>().ToArray();
            Assert.Equal([6_000d, 7_000d, 8_000d], activations.Select(value => value.TimeMilliseconds));
            Assert.All(activations, activation =>
            {
                Assert.Equal((byte)4u, activation.Phase);
                Assert.Same(effect, Assert.Single(activation.Effects));
            });
            SpellEffectExpiration expiration = Assert.Single(events.OfType<SpellEffectExpiration>());
            Assert.Equal(8_000d, expiration.TimeMilliseconds);
            Assert.False(timeline.HasPendingEffect);
        }

        [Fact]
        public void PeriodicEffect_WithDurationShorterThanTickExpiresWithoutInvocation()
        {
            var timeline = new SpellEffectTimeline(0u);
            var effect = new Spell4EffectsEntry
            {
                TickTime     = 1_001u,
                DurationTime = 1_000u
            };

            timeline.Register(effect, byte.MaxValue, 1u);
            IReadOnlyList<SpellEffectTimelineEvent> events = timeline.Advance(1d);

            Assert.Empty(events.OfType<SpellEffectActivation>());
            Assert.Single(events.OfType<SpellEffectExpiration>());
            Assert.False(timeline.HasPendingEffect);
        }

        [Fact]
        public void Registrations_RemainIndependentAcrossRepeatedExecutionsAndPhases()
        {
            var timeline = new SpellEffectTimeline(0u);
            var effect = new Spell4EffectsEntry();

            Assert.Equal(SpellEffectRegistrationResult.Registered, timeline.Register(effect, (byte)0u, 1u));
            Assert.Equal(SpellEffectRegistrationResult.Registered, timeline.Register(effect, (byte)0u, 2u));
            Assert.Equal(SpellEffectRegistrationResult.Registered, timeline.Register(effect, (byte)1u, 3u));

            SpellEffectActivation[] activations = timeline.Advance(0d)
                .OfType<SpellEffectActivation>()
                .ToArray();
            Assert.Collection(
                activations,
                activation =>
                {
                    Assert.Equal((byte)0u, activation.Phase);
                    Assert.Equal(1u, activation.ActivationId);
                },
                activation =>
                {
                    Assert.Equal((byte)0u, activation.Phase);
                    Assert.Equal(2u, activation.ActivationId);
                },
                activation =>
                {
                    Assert.Equal((byte)1u, activation.Phase);
                    Assert.Equal(3u, activation.ActivationId);
                });
        }

        [Fact]
        public void OpenPeriodicEffect_RequiresRootBoundOrClientCancellation()
        {
            var unsupported = new SpellEffectTimeline(0u);
            var unsupportedEffect = new Spell4EffectsEntry { TickTime = 200u };
            Assert.Equal(
                SpellEffectRegistrationResult.UnsupportedOpenEnded,
                unsupported.Register(unsupportedEffect, byte.MaxValue, 1u));
            Assert.False(unsupported.HasPendingEffect);

            var rootBound = new SpellEffectTimeline(1_000u);
            var rootBoundEffect = new Spell4EffectsEntry { TickTime = 200u };
            Assert.Equal(
                SpellEffectRegistrationResult.Registered,
                rootBound.Register(rootBoundEffect, byte.MaxValue, 1u));
            IReadOnlyList<SpellEffectTimelineEvent> rootEvents = rootBound.Advance(1d);
            Assert.Equal(5, rootEvents.OfType<SpellEffectActivation>().Count());
            Assert.Single(rootEvents.OfType<SpellEffectExpiration>());
            Assert.False(rootBound.HasPendingEffect);

            var cancellable = new SpellEffectTimeline(0u);
            var cancellableEffect = new Spell4EffectsEntry
            {
                TickTime = 200u,
                Flags    = (uint)SpellEffectFlags.CancelOnly
            };
            Assert.Equal(
                SpellEffectRegistrationResult.Registered,
                cancellable.Register(cancellableEffect, byte.MaxValue, 1u));
            Assert.Equal(5, cancellable.Advance(1d).OfType<SpellEffectActivation>().Count());
            Assert.True(cancellable.HasPendingEffect);

            cancellable.Cancel();
            Assert.False(cancellable.HasPendingEffect);
            Assert.Empty(cancellable.Advance(10d));
        }

        [Fact]
        public void CatchUp_IsBoundedAndDueWorkDrainsWithoutDiscardingRemainder()
        {
            var timeline = new SpellEffectTimeline(0u);
            var effect = new Spell4EffectsEntry
            {
                TickTime = 1u,
                Flags    = (uint)SpellEffectFlags.CancelOnly
            };
            timeline.Register(effect, byte.MaxValue, 1u);

            int firstCount = timeline.Advance(double.MaxValue)
                .OfType<SpellEffectActivation>()
                .Sum(activation => activation.Effects.Count);
            int secondCount = timeline.Advance(0d)
                .OfType<SpellEffectActivation>()
                .Sum(activation => activation.Effects.Count);

            Assert.Equal(SpellEffectTimeline.MaximumCallbacksPerUpdate, firstCount);
            Assert.Equal(SpellEffectTimeline.MaximumCallbacksPerUpdate, secondCount);
            Assert.True(timeline.HasPendingEffect);
        }

        [Fact]
        public void SimultaneousExpirations_AreBoundedAndDrainOnLaterUpdate()
        {
            var timeline = new SpellEffectTimeline(0u);
            for (ulong activationId = 0u;
                activationId < (ulong)SpellEffectTimeline.MaximumCallbacksPerUpdate * 2u;
                activationId++)
            {
                timeline.Register(new Spell4EffectsEntry
                {
                    TickTime     = 1_001u,
                    DurationTime = 1_000u
                }, byte.MaxValue, activationId);
            }

            IReadOnlyList<SpellEffectTimelineEvent> first = timeline.Advance(1d);
            IReadOnlyList<SpellEffectTimelineEvent> second = timeline.Advance(0d);

            Assert.Equal(
                SpellEffectTimeline.MaximumCallbacksPerUpdate,
                first.OfType<SpellEffectExpiration>().Count());
            Assert.Equal(
                SpellEffectTimeline.MaximumCallbacksPerUpdate,
                second.OfType<SpellEffectExpiration>().Count());
            Assert.False(timeline.HasPendingEffect);
        }

        [Fact]
        public void LargeFiniteElapsedTime_IsRetainedBeyondCatchUpActivationBound()
        {
            var timeline = new SpellEffectTimeline(0u);
            var effect = new Spell4EffectsEntry { DelayTime = 301_000u };
            timeline.Register(effect, byte.MaxValue, 1u);

            SpellEffectActivation activation = Assert.Single(
                timeline.Advance(301d).OfType<SpellEffectActivation>());

            Assert.Equal(301_000d, activation.TimeMilliseconds);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1d)]
        public void InvalidElapsedTime_DoesNotAdvanceOrDiscardPendingEffect(double elapsedSeconds)
        {
            var timeline = new SpellEffectTimeline(0u);
            var effect = new Spell4EffectsEntry { DelayTime = 1_000u };
            timeline.Register(effect, byte.MaxValue, 1u);

            Assert.Empty(timeline.Advance(elapsedSeconds));
            Assert.Empty(timeline.Advance(0.999d));
            Assert.Single(timeline.Advance(0.001d).OfType<SpellEffectActivation>());
        }
    }
}
