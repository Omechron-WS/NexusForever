using Microsoft.Extensions.Options;
using Moq;
using NexusForever.WorldServer.Command;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Web.Configuration;

namespace NexusForever.WorldServer.Tests.Command
{
    public class BoundedPendingCommandQueueTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Constructor_NonPositiveCapacity_ThrowsArgumentOutOfRangeException(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedPendingCommandQueue(capacity));
        }

        [Fact]
        public async Task TryEnqueue_ConcurrentProducersCannotExceedCapacity()
        {
            var queue = new BoundedPendingCommandQueue(10);
            ICommandContext context = Mock.Of<ICommandContext>();
            using var start = new ManualResetEventSlim();
            int admitted = 0;
            Task[] producers = Enumerable.Range(0, 100)
                .Select(index => Task.Run(() =>
                {
                    start.Wait();
                    if (queue.TryEnqueue(new PendingCommand(context, index.ToString())))
                        Interlocked.Increment(ref admitted);
                }))
                .ToArray();

            start.Set();
            await Task.WhenAll(producers);

            Assert.Equal(10, admitted);
            Assert.Equal(10, queue.Count);
        }

        [Fact]
        public void TryDequeue_ReleasesCapacityForNextCommand()
        {
            var queue = new BoundedPendingCommandQueue(1);
            ICommandContext context = Mock.Of<ICommandContext>();
            var first = new PendingCommand(context, "first");
            var second = new PendingCommand(context, "second");

            Assert.True(queue.TryEnqueue(first));
            Assert.False(queue.TryEnqueue(second));
            Assert.True(queue.TryDequeue(out PendingCommand dequeued));
            Assert.Same(first, dequeued);
            Assert.True(queue.TryEnqueue(second));
            Assert.Equal(1, queue.Count);
        }

        [Fact]
        public void Complete_ClearsPendingCommandsAndRejectsNewCommands()
        {
            var queue = new BoundedPendingCommandQueue(1);
            ICommandContext context = Mock.Of<ICommandContext>();
            Assert.True(queue.TryEnqueue(new PendingCommand(context, "first")));

            queue.Complete();

            Assert.Equal(0, queue.Count);
            Assert.False(queue.TryEnqueue(new PendingCommand(context, "second")));
        }

        [Fact]
        public void CommandManager_HandleCommandDelay_ReportsBoundedAdmission()
        {
            var manager = new CommandManager(Options.Create(new WebSocketCommandOptions
            {
                MaximumPendingCommands = 1
            }));
            ICommandContext context = Mock.Of<ICommandContext>();

            Assert.True(manager.HandleCommandDelay(context, "first"));
            Assert.False(manager.HandleCommandDelay(context, "second"));

            manager.Shutdown();
        }
    }
}
