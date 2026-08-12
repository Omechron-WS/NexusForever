using NexusForever.Game;

namespace NexusForever.Game.Tests
{
    public sealed class ShutdownManagerTests
    {
        [Theory]
        [InlineData(0d)]
        [InlineData(-1d)]
        public void StartShutdown_NonPositiveDelayIsRejectedWithoutPendingState(double seconds)
        {
            var manager = new ShutdownManager();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                manager.StartShutdown(TimeSpan.FromSeconds(seconds)));

            Assert.False(manager.IsShutdownPending);
        }
    }
}
