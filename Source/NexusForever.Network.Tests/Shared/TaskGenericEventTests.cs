using NexusForever.Shared.Game.Events;

namespace NexusForever.Network.Tests.Shared
{
    public class TaskGenericEventTests
    {
        [Fact]
        public void Execute_FaultedTask_InvokesFailureCallback()
        {
            var expected = new InvalidOperationException("failure");
            Exception actual = null;
            bool succeeded = false;
            var taskEvent = new TaskGenericEvent<int>(Task.FromException<int>(expected),
                _ => succeeded = true,
                exception => actual = exception);

            taskEvent.Execute();

            Assert.False(succeeded);
            Assert.Same(expected, actual);
        }

        [Fact]
        public void Execute_CallbackFailure_InvokesFailureCallback()
        {
            var expected = new InvalidOperationException("failure");
            Exception actual = null;
            var taskEvent = new TaskGenericEvent<int>(Task.FromResult(1),
                _ => throw expected,
                exception => actual = exception);

            taskEvent.Execute();

            Assert.Same(expected, actual);
        }
    }
}
