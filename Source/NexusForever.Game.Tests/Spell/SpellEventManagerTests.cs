using NexusForever.Game.Abstract.Spell.Event;
using NexusForever.Game.Spell.Event;

namespace NexusForever.Game.Tests.Spell
{
    public sealed class SpellEventManagerTests
    {
        private const double Tick = 0.25d;

        [Fact]
        public void PriorCallbackCancelSkipsLaterSnapshotEventBeforeUpdate()
        {
            var manager = new SpellEventManager();
            var operations = new List<string>();
            TestSpellEvent first = CreateDueEvent(
                "first",
                operations,
                stage =>
                {
                    if (stage == ObserverStage.CallbackAction)
                        manager.CancelEvents();
                });
            TestSpellEvent later = CreateDueEvent("later", operations);
            manager.EnqueueEvent(first);
            manager.EnqueueEvent(later);

            manager.Update(Tick);

            Assert.Equal(SuccessfulOperations("first"), operations);
            Assert.False(manager.HasPendingEvent);
        }

        [Theory]
        [InlineData(ObserverStage.Update)]
        [InlineData(ObserverStage.DelayGetter)]
        [InlineData(ObserverStage.CallbackGetter)]
        [InlineData(ObserverStage.CallbackAction)]
        public void CancelAndSameEventReenqueueAtObserverStageDefersAndPreservesReplacement(
            ObserverStage replacementStage)
        {
            var manager = new SpellEventManager();
            var operations = new List<string>();
            bool replacementEnqueued = false;
            TestSpellEvent spellEvent = null;
            spellEvent = CreateDueEvent(
                "event",
                operations,
                stage =>
                {
                    if (stage != replacementStage || replacementEnqueued)
                        return;

                    replacementEnqueued = true;
                    manager.CancelEvents();
                    manager.EnqueueEvent(spellEvent);
                });
            manager.EnqueueEvent(spellEvent);

            manager.Update(Tick);

            string[] firstTickOperations = OperationsThrough("event", replacementStage);
            Assert.True(replacementEnqueued);
            Assert.Equal(firstTickOperations, operations);
            Assert.True(manager.HasPendingEvent);

            manager.Update(Tick);

            Assert.Equal(
                firstTickOperations.Concat(SuccessfulOperations("event")),
                operations);
            Assert.False(manager.HasPendingEvent);
        }

        [Theory]
        [InlineData(ObserverStage.Update)]
        [InlineData(ObserverStage.DelayGetter)]
        [InlineData(ObserverStage.CallbackGetter)]
        [InlineData(ObserverStage.CallbackAction)]
        public void ThrowOnceAtObserverStagePropagatesRetainsAndRetriesBeforeLaterEvent(
            ObserverStage failureStage)
        {
            var manager = new SpellEventManager();
            var operations = new List<string>();
            bool hasThrown = false;
            TestSpellEvent first = CreateDueEvent(
                "first",
                operations,
                stage =>
                {
                    if (stage != failureStage || hasThrown)
                        return;

                    hasThrown = true;
                    throw new InvalidOperationException("Test observer failure.");
                });
            TestSpellEvent later = CreateDueEvent("later", operations);
            manager.EnqueueEvent(first);
            manager.EnqueueEvent(later);

            Assert.Throws<InvalidOperationException>(() => manager.Update(Tick));

            string[] failedTickOperations = OperationsThrough("first", failureStage);
            Assert.True(hasThrown);
            Assert.Equal(failedTickOperations, operations);
            Assert.True(manager.HasPendingEvent);

            manager.Update(Tick);

            Assert.Equal(
                failedTickOperations
                    .Concat(SuccessfulOperations("first"))
                    .Concat(SuccessfulOperations("later")),
                operations);
            Assert.False(manager.HasPendingEvent);
        }

        [Fact]
        public void DistinctAdditionDuringUpdateWaitsForNextSnapshot()
        {
            var manager = new SpellEventManager();
            var operations = new List<string>();
            TestSpellEvent added = CreateDueEvent("added", operations);
            bool hasAdded = false;
            TestSpellEvent first = CreateDueEvent(
                "first",
                operations,
                stage =>
                {
                    if (stage != ObserverStage.Update || hasAdded)
                        return;

                    hasAdded = true;
                    manager.EnqueueEvent(added);
                });
            manager.EnqueueEvent(first);

            manager.Update(Tick);

            Assert.Equal(SuccessfulOperations("first"), operations);
            Assert.True(manager.HasPendingEvent);

            manager.Update(Tick);

            Assert.Equal(
                SuccessfulOperations("first").Concat(SuccessfulOperations("added")),
                operations);
            Assert.False(manager.HasPendingEvent);
        }

        private static TestSpellEvent CreateDueEvent(
            string name,
            ICollection<string> operations,
            Action<ObserverStage> observe = null)
        {
            return new TestSpellEvent(
                _ =>
                {
                    operations.Add($"{name}-update");
                    observe?.Invoke(ObserverStage.Update);
                },
                () =>
                {
                    operations.Add($"{name}-delay");
                    observe?.Invoke(ObserverStage.DelayGetter);
                    return 0d;
                },
                () =>
                {
                    operations.Add($"{name}-callback-getter");
                    observe?.Invoke(ObserverStage.CallbackGetter);
                    return () =>
                    {
                        operations.Add($"{name}-callback");
                        observe?.Invoke(ObserverStage.CallbackAction);
                    };
                });
        }

        private static string[] SuccessfulOperations(string name)
        {
            return
            [
                $"{name}-update",
                $"{name}-delay",
                $"{name}-callback-getter",
                $"{name}-callback"
            ];
        }

        private static string[] OperationsThrough(string name, ObserverStage stage)
        {
            return stage switch
            {
                ObserverStage.Update => [$"{name}-update"],
                ObserverStage.DelayGetter => [$"{name}-update", $"{name}-delay"],
                ObserverStage.CallbackGetter =>
                [
                    $"{name}-update",
                    $"{name}-delay",
                    $"{name}-callback-getter"
                ],
                ObserverStage.CallbackAction => SuccessfulOperations(name),
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            };
        }

        public enum ObserverStage
        {
            Update,
            DelayGetter,
            CallbackGetter,
            CallbackAction
        }

        private sealed class TestSpellEvent : ISpellEvent
        {
            public double Delay => getDelay();
            public Action Callback => getCallback();

            private readonly Action<double> update;
            private readonly Func<double> getDelay;
            private readonly Func<Action> getCallback;

            public TestSpellEvent(
                Action<double> update,
                Func<double> getDelay,
                Func<Action> getCallback)
            {
                this.update      = update;
                this.getDelay    = getDelay;
                this.getCallback = getCallback;
            }

            public void Update(double lastTick)
            {
                update(lastTick);
            }
        }
    }
}
