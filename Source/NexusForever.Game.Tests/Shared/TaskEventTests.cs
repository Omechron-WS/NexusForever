using NexusForever.Shared.Game.Events;

namespace NexusForever.Game.Tests.Shared
{
    public class TaskEventTests
    {
        [Fact]
        public void Execute_CompletedTask_InvokesSuccessCallback()
        {
            bool succeeded = false;
            bool failed = false;
            var taskEvent = new TaskEvent(Task.CompletedTask,
                () => succeeded = true,
                _ => failed = true);

            taskEvent.Execute();

            Assert.True(succeeded);
            Assert.False(failed);
        }

        [Fact]
        public void Execute_FaultedTask_InvokesFailureCallbackWithoutSuccess()
        {
            var expected = new InvalidOperationException("Save failed.");
            bool succeeded = false;
            Exception failure = null;
            var taskEvent = new TaskEvent(Task.FromException(expected),
                () => succeeded = true,
                exception => failure = exception);

            taskEvent.Execute();

            Assert.False(succeeded);
            Assert.Same(expected, failure);
        }

        [Fact]
        public void Execute_FaultedTaskWithoutFailureCallback_PropagatesException()
        {
            var expected = new InvalidOperationException("Save failed.");
            var taskEvent = new TaskEvent(Task.FromException(expected), () => { });

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(taskEvent.Execute);

            Assert.Same(expected, actual);
        }

        [Fact]
        public void Execute_CancelledTask_InvokesFailureCallbackWithoutSuccess()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool succeeded = false;
            Exception failure = null;
            var taskEvent = new TaskEvent(Task.FromCanceled(cancellation.Token),
                () => succeeded = true,
                exception => failure = exception);

            taskEvent.Execute();

            Assert.False(succeeded);
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
        }

        [Fact]
        public void Execute_SuccessCallbackThrows_PropagatesException()
        {
            var expected = new InvalidOperationException("Callback failed.");
            bool failureCallbackInvoked = false;
            var taskEvent = new TaskEvent(Task.CompletedTask,
                () => throw expected,
                _ => failureCallbackInvoked = true);

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(taskEvent.Execute);

            Assert.Same(expected, actual);
            Assert.False(failureCallbackInvoked);
        }

        [Fact]
        public void CanExecute_IncompleteTask_ReturnsFalse()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var taskEvent = new TaskEvent(completion.Task, () => { });

            Assert.False(taskEvent.CanExecute());

            completion.SetResult();
            Assert.True(taskEvent.CanExecute());
        }
    }
}
