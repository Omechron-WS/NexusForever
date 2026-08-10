using NexusForever.Database;

namespace NexusForever.Game.Tests.Persistence
{
    public class SaveCommitAcknowledgementTests
    {
        [Fact]
        public void Acknowledge_ExecutesEveryActionInRegistrationOrderAndAggregatesFailures()
        {
            var firstException = new InvalidOperationException("first failure");
            var secondException = new ArgumentException("second failure");
            var executionOrder = new List<int>();
            var scope = new SaveCommitScope();
            scope.Register(() => executionOrder.Add(1));
            scope.Register(() =>
            {
                executionOrder.Add(2);
                throw firstException;
            });
            scope.Register(() => executionOrder.Add(3));
            scope.Register(() =>
            {
                executionOrder.Add(4);
                throw secondException;
            });
            SaveCommitAcknowledgement acknowledgement = scope.CreateAcknowledgement();

            AggregateException exception = Assert.Throws<AggregateException>(acknowledgement.Acknowledge);

            Assert.Equal([1, 2, 3, 4], executionOrder);
            Assert.Contains(firstException, exception.InnerExceptions);
            Assert.Contains(secondException, exception.InnerExceptions);

            acknowledgement.Acknowledge();
            Assert.Equal([1, 2, 3, 4], executionOrder);
        }

        [Fact]
        public void CreateAcknowledgement_SealsScope()
        {
            var scope = new SaveCommitScope();
            scope.Register(() => { });

            scope.CreateAcknowledgement();

            Assert.Throws<InvalidOperationException>(() => scope.Register(() => { }));
            Assert.Throws<InvalidOperationException>(scope.CreateAcknowledgement);
        }

        [Fact]
        public async Task Acknowledge_ConcurrentCallsExecuteActionOnce()
        {
            int executionCount = 0;
            var scope = new SaveCommitScope();
            scope.Register(() => Interlocked.Increment(ref executionCount));
            SaveCommitAcknowledgement acknowledgement = scope.CreateAcknowledgement();

            await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(_ => Task.Run(acknowledgement.Acknowledge)));

            Assert.Equal(1, executionCount);
        }

        [Fact]
        public async Task Acknowledge_EmptyScopeIsSafeForConcurrentCalls()
        {
            SaveCommitAcknowledgement acknowledgement = new SaveCommitScope().CreateAcknowledgement();

            await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(_ => Task.Run(acknowledgement.Acknowledge)));
        }
    }
}
