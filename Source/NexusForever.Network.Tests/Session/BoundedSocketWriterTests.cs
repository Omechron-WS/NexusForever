using System.Threading.Channels;
using NexusForever.Network.Session;

namespace NexusForever.Network.Tests.Session
{
    public class BoundedSocketWriterTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(-1, 1)]
        [InlineData(1, 0)]
        [InlineData(1, -1)]
        public void Constructor_NonPositiveCapacity_ThrowsArgumentOutOfRangeException(int bytes, int frames)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BoundedSocketWriter(new ControlledSendAdapter(), bytes, frames));
        }

        [Fact]
        public async Task Writer_PartialSends_PreservesFrameOrderWithSingleWriter()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 32, 4);

            Assert.True(writer.TryEnqueue([0x01, 0x02, 0x03]));
            Assert.True(writer.TryEnqueue([0x04, 0x05]));
            writer.Complete();

            SendRequest first = await adapter.NextRequestAsync();
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, first.Buffer);
            Assert.False(adapter.TryGetRequest(out _));
            first.Complete(1);

            SendRequest firstRemainder = await adapter.NextRequestAsync();
            Assert.Equal(new byte[] { 0x02, 0x03 }, firstRemainder.Buffer);
            firstRemainder.Complete(2);

            SendRequest second = await adapter.NextRequestAsync();
            Assert.Equal(new byte[] { 0x04, 0x05 }, second.Buffer);
            second.Complete(1);

            SendRequest secondRemainder = await adapter.NextRequestAsync();
            Assert.Equal(new byte[] { 0x05 }, secondRemainder.Buffer);
            secondRemainder.Complete(1);

            await writer.Completion;

            Assert.Equal(1, adapter.MaximumConcurrentSends);
            Assert.Equal(0L, writer.PendingBytes);
            Assert.Equal(0, writer.PendingFrames);
        }

        [Fact]
        public async Task TryEnqueue_ByteLimitIncludesInFlightFrameAndReleasesAfterSend()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 5, 8);

            Assert.True(writer.TryEnqueue([0x01, 0x02, 0x03]));
            SendRequest inFlight = await adapter.NextRequestAsync();

            Assert.True(writer.TryEnqueue([0x04, 0x05]));
            Assert.False(writer.TryEnqueue([0x06]));
            Assert.Equal(5L, writer.PendingBytes);

            inFlight.Complete(3);
            SendRequest queued = await adapter.NextRequestAsync();

            Assert.True(writer.TryEnqueue([0x06, 0x07, 0x08]));
            Assert.Equal(5L, writer.PendingBytes);

            writer.Abort();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.Completion);
            Assert.Equal(0L, writer.PendingBytes);
            Assert.Equal(0, writer.PendingFrames);
            Assert.True(queued.CancellationToken.IsCancellationRequested);
        }

        [Fact]
        public async Task TryEnqueue_FrameLimitIncludesInFlightFrame()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 64, 2);

            Assert.True(writer.TryEnqueue([0x01]));
            SendRequest inFlight = await adapter.NextRequestAsync();
            Assert.True(writer.TryEnqueue([0x02]));
            Assert.False(writer.TryEnqueue([0x03]));
            Assert.Equal(2, writer.PendingFrames);

            writer.Abort();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.Completion);
            Assert.True(inFlight.CancellationToken.IsCancellationRequested);
        }

        [Fact]
        public async Task Complete_RejectsNewFramesAndDrainsAdmittedFrames()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 64, 2);

            Assert.True(writer.TryEnqueue([0x01]));
            writer.Complete();
            Assert.False(writer.TryEnqueue([0x02]));

            SendRequest request = await adapter.NextRequestAsync();
            request.Complete(1);
            await writer.Completion;
        }

        [Fact]
        public async Task SendFailure_FaultsWriterAndRejectsNewFrames()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 64, 2);

            Assert.True(writer.TryEnqueue([0x01]));
            SendRequest request = await adapter.NextRequestAsync();
            request.Fail(new IOException("send failed"));

            IOException exception = await Assert.ThrowsAsync<IOException>(() => writer.Completion);
            Assert.Equal("send failed", exception.Message);
            Assert.False(writer.TryEnqueue([0x02]));
            Assert.Equal(0L, writer.PendingBytes);
            Assert.Equal(0, writer.PendingFrames);
        }

        [Fact]
        public async Task ZeroByteSend_FaultsWriter()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 64, 2);

            Assert.True(writer.TryEnqueue([0x01]));
            SendRequest request = await adapter.NextRequestAsync();
            request.Complete(0);

            IOException exception = await Assert.ThrowsAsync<IOException>(() => writer.Completion);
            Assert.Contains("invalid byte count", exception.Message);
        }

        [Fact]
        public async Task TryEnqueue_ConcurrentProducersCannotExceedByteLimit()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 10, 100);
            using var start = new ManualResetEventSlim();
            int admitted = 0;
            Task[] producers = Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    if (writer.TryEnqueue([0x01]))
                        Interlocked.Increment(ref admitted);
                }))
                .ToArray();

            start.Set();
            await Task.WhenAll(producers);

            Assert.Equal(10, admitted);
            Assert.Equal(10L, writer.PendingBytes);
            Assert.Equal(10, writer.PendingFrames);

            writer.Abort();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.Completion);
        }

        [Fact]
        public async Task TryEnqueue_ConcurrentProducersCannotExceedFrameLimit()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 100, 10);
            using var start = new ManualResetEventSlim();
            int admitted = 0;
            Task[] producers = Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    if (writer.TryEnqueue([0x01]))
                        Interlocked.Increment(ref admitted);
                }))
                .ToArray();

            start.Set();
            await Task.WhenAll(producers);

            Assert.Equal(10, admitted);
            Assert.Equal(10L, writer.PendingBytes);
            Assert.Equal(10, writer.PendingFrames);

            writer.Abort();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.Completion);
        }

        [Fact]
        public async Task Abort_CancelsInFlightSendAndRejectsNewFrames()
        {
            var adapter = new ControlledSendAdapter();
            var writer = new BoundedSocketWriter(adapter, 64, 2);

            Assert.True(writer.TryEnqueue([0x01]));
            SendRequest request = await adapter.NextRequestAsync();
            writer.Abort();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.Completion);
            Assert.True(request.CancellationToken.IsCancellationRequested);
            Assert.False(writer.TryEnqueue([0x02]));
            Assert.Equal(0L, writer.PendingBytes);
            Assert.Equal(0, writer.PendingFrames);
        }

        private sealed class ControlledSendAdapter : ISocketSendAdapter
        {
            private readonly Channel<SendRequest> requests = Channel.CreateUnbounded<SendRequest>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader                  = true,
                SingleWriter                  = true
            });

            private int activeSends;
            private int maximumConcurrentSends;

            public int MaximumConcurrentSends => Volatile.Read(ref maximumConcurrentSends);

            public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            {
                int active = Interlocked.Increment(ref activeSends);
                UpdateMaximumConcurrentSends(active);

                var request = new SendRequest(buffer.ToArray(), cancellationToken);
                if (!requests.Writer.TryWrite(request))
                    throw new InvalidOperationException();

                return new ValueTask<int>(WaitForCompletionAsync(request));
            }

            public ValueTask<SendRequest> NextRequestAsync()
            {
                return requests.Reader.ReadAsync();
            }

            public bool TryGetRequest(out SendRequest request)
            {
                return requests.Reader.TryRead(out request);
            }

            private async Task<int> WaitForCompletionAsync(SendRequest request)
            {
                try
                {
                    return await request.Completion;
                }
                finally
                {
                    Interlocked.Decrement(ref activeSends);
                }
            }

            private void UpdateMaximumConcurrentSends(int active)
            {
                while (true)
                {
                    int current = Volatile.Read(ref maximumConcurrentSends);
                    if (active <= current)
                        return;

                    if (Interlocked.CompareExchange(ref maximumConcurrentSends, active, current) == current)
                        return;
                }
            }
        }

        private sealed class SendRequest
        {
            private readonly TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenRegistration cancellationRegistration;

            public byte[] Buffer { get; }
            public CancellationToken CancellationToken { get; }
            public Task<int> Completion => completion.Task;

            public SendRequest(byte[] buffer, CancellationToken cancellationToken)
            {
                Buffer                 = buffer;
                CancellationToken      = cancellationToken;
                cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            }

            public void Complete(int sent)
            {
                cancellationRegistration.Dispose();
                completion.TrySetResult(sent);
            }

            public void Fail(Exception exception)
            {
                cancellationRegistration.Dispose();
                completion.TrySetException(exception);
            }
        }
    }
}
