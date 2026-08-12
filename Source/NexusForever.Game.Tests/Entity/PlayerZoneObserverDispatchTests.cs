using NexusForever.Game.Entity;

namespace NexusForever.Game.Tests.Entity
{
    public sealed class PlayerZoneObserverDispatchTests
    {
        [Fact]
        public void DispatchZoneObservers_QuestRunsFirstAndFailuresDoNotSuppressLaterObservers()
        {
            var order = new List<string>();
            var failures = new List<(string Observer, Exception Exception)>();
            var questFailure = new InvalidOperationException("quest failure");
            var chatFailure = new InvalidOperationException("chat failure");

            Player.DispatchZoneObservers(
                () => true,
                () =>
                {
                    order.Add("quest");
                    throw questFailure;
                },
                () => order.Add("zone map"),
                () =>
                {
                    order.Add("chat");
                    throw chatFailure;
                },
                () => order.Add("tutorial"),
                () => order.Add("presence"),
                (observer, exception) => failures.Add((observer, exception)));

            Assert.Equal(["quest", "zone map", "chat", "tutorial", "presence"], order);
            Assert.Collection(
                failures,
                failure =>
                {
                    Assert.Equal("quest", failure.Observer);
                    Assert.Same(questFailure, failure.Exception);
                },
                failure =>
                {
                    Assert.Equal("chat", failure.Observer);
                    Assert.Same(chatFailure, failure.Exception);
                });
        }

        [Fact]
        public void DispatchZoneObservers_TransitionInvalidatedByObserverSuppressesRemainingObservers()
        {
            var order = new List<string>();
            bool isCurrent = true;
            int failureCount = 0;

            Player.DispatchZoneObservers(
                () => isCurrent,
                () =>
                {
                    order.Add("quest");
                    isCurrent = false;
                },
                () => order.Add("zone map"),
                () => order.Add("chat"),
                () => order.Add("tutorial"),
                () => order.Add("presence"),
                (_, _) => failureCount++);

            Assert.Equal(["quest"], order);
            Assert.Equal(0, failureCount);
        }

        [Fact]
        public void DispatchZoneObservers_StaleTransitionDoesNotInvokeAnyObserver()
        {
            int observerCalls = 0;
            int failureCount = 0;

            Player.DispatchZoneObservers(
                () => false,
                () => observerCalls++,
                () => observerCalls++,
                () => observerCalls++,
                () => observerCalls++,
                () => observerCalls++,
                (_, _) => failureCount++);

            Assert.Equal(0, observerCalls);
            Assert.Equal(0, failureCount);
        }
    }
}
