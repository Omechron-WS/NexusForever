using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Web;

namespace NexusForever.WorldServer.Tests.Web
{
    public class WebSocketResponseQueueTests
    {
        [Theory]
        [InlineData(0, 1, 1d)]
        [InlineData(-1, 1, 1d)]
        [InlineData(1, 0, 1d)]
        [InlineData(1, -1, 1d)]
        [InlineData(1, 1, 0d)]
        [InlineData(1, 1, 31d)]
        public void Constructor_InvalidBound_ThrowsArgumentOutOfRangeException(int bytes, int responses, double timeoutSeconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WebSocketResponseQueue(
                new ControlledWebSocket(),
                bytes,
                responses,
                TimeSpan.FromSeconds(timeoutSeconds),
                NullLogger.Instance));
        }

        [Fact]
        public async Task ResponsePump_PreservesFifoWithOneActiveSend()
        {
            var webSocket = new ControlledWebSocket();
            var queue = CreateQueue(webSocket, 1_024, 4);

            Assert.True(queue.TryEnqueue("first", "info"));
            Assert.True(queue.TryEnqueue("second", "error"));

            SendRequest first = await webSocket.NextSendAsync();
            Assert.Equal(BuildExpectedResponse("first", "info"), first.Buffer);
            Assert.False(webSocket.TryGetSend(out _));
            first.Complete();

            SendRequest second = await webSocket.NextSendAsync();
            Assert.Equal(BuildExpectedResponse("second", "error"), second.Buffer);
            second.Complete();

            await queue.StopAsync();

            Assert.Equal(1, webSocket.MaximumConcurrentSends);
            Assert.Equal(0L, queue.PendingBytes);
            Assert.Equal(0, queue.PendingResponses);
        }

        [Fact]
        public async Task TryEnqueue_MessageLimitIncludesInFlightResponse()
        {
            var webSocket = new ControlledWebSocket();
            var queue = CreateQueue(webSocket, 1_024, 1);

            Assert.True(queue.TryEnqueue("first", "info"));
            SendRequest inFlight = await webSocket.NextSendAsync();
            Assert.False(queue.TryEnqueue("second", "info"));
            Assert.Equal(1, queue.PendingResponses);

            inFlight.Complete();
            await queue.StopAsync();
        }

        [Fact]
        public async Task TryEnqueue_ByteLimitIncludesInFlightResponse()
        {
            byte[] expected = BuildExpectedResponse("first", "info");
            var webSocket = new ControlledWebSocket();
            var queue = CreateQueue(webSocket, expected.Length, 4);

            Assert.True(queue.TryEnqueue("first", "info"));
            SendRequest inFlight = await webSocket.NextSendAsync();
            Assert.False(queue.TryEnqueue("second", "info"));
            Assert.Equal(expected.Length, queue.PendingBytes);
            Assert.Equal(1, queue.PendingResponses);

            inFlight.Complete();
            await queue.StopAsync();
        }

        [Fact]
        public async Task OversizedSingleResponse_FailsClosedWithoutStartingSend()
        {
            var webSocket = new ControlledWebSocket();
            var queue = CreateQueue(webSocket, 32, 4);
            var context = new WebSocketCommandContext(queue, []);

            context.SendMessage(new string('x', 33));

            Assert.Equal(WebSocketState.Aborted, webSocket.State);
            Assert.False(webSocket.TryGetSend(out _));
            Assert.Equal(0L, queue.PendingBytes);
            Assert.Equal(0, queue.PendingResponses);
            await queue.StopAsync();
        }

        [Fact]
        public async Task TryEnqueue_ConcurrentProducersCannotExceedEitherLimit()
        {
            int responseSize = BuildExpectedResponse("value", "info").Length;
            var webSocket = new ControlledWebSocket();
            var queue = CreateQueue(webSocket, responseSize * 10, 10);
            using var start = new ManualResetEventSlim();
            int admitted = 0;
            Task[] producers = Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    if (queue.TryEnqueue("value", "info"))
                        Interlocked.Increment(ref admitted);
                }))
                .ToArray();

            start.Set();
            await Task.WhenAll(producers);

            Assert.Equal(10, admitted);
            Assert.Equal(responseSize * 10, queue.PendingBytes);
            Assert.Equal(10, queue.PendingResponses);

            SendRequest inFlight = await webSocket.NextSendAsync();
            Task stop = queue.StopAsync();
            await stop;
            Assert.True(inFlight.CancellationToken.IsCancellationRequested);
            Assert.Equal(0L, queue.PendingBytes);
            Assert.Equal(0, queue.PendingResponses);
        }

        [Fact]
        public async Task FailClosed_AbortsSocketCancelsSendAndRejectsNewResponses()
        {
            var webSocket = new ControlledWebSocket();
            var queue = CreateQueue(webSocket, 1_024, 4);
            Assert.True(queue.TryEnqueue("first", "info"));
            SendRequest inFlight = await webSocket.NextSendAsync();

            queue.FailClosed();
            await queue.StopAsync();

            Assert.Equal(WebSocketState.Aborted, webSocket.State);
            Assert.True(inFlight.CancellationToken.IsCancellationRequested);
            Assert.False(queue.TryEnqueue("second", "info"));
        }

        [Fact]
        public async Task SendFailure_FaultsPumpAndAbortsSocket()
        {
            var webSocket = new ControlledWebSocket();
            var queue = CreateQueue(webSocket, 1_024, 4);
            Assert.True(queue.TryEnqueue("first", "info"));
            SendRequest inFlight = await webSocket.NextSendAsync();

            inFlight.Fail(new IOException("send failed"));

            IOException exception = await Assert.ThrowsAsync<IOException>(() => queue.Completion);
            Assert.Equal("send failed", exception.Message);
            Assert.Equal(WebSocketState.Aborted, webSocket.State);
            Assert.Equal(0L, queue.PendingBytes);
            Assert.Equal(0, queue.PendingResponses);
            await queue.StopAsync();
        }

        [Fact]
        public async Task StopAsync_UncooperativeSendTimesOutAndAbortsWithoutHangingIngress()
        {
            var webSocket = new ControlledWebSocket(ignoreCancellation: true);
            var queue = CreateQueue(webSocket, 1_024, 4, TimeSpan.FromMilliseconds(20));
            Assert.True(queue.TryEnqueue("first", "info"));
            SendRequest inFlight = await webSocket.NextSendAsync();

            await queue.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(WebSocketState.Aborted, webSocket.State);
            Assert.False(queue.TryEnqueue("second", "info"));

            inFlight.Complete();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.Completion);
        }

        private static WebSocketResponseQueue CreateQueue(
            ControlledWebSocket webSocket,
            int maximumPendingBytes,
            int maximumPendingResponses,
            TimeSpan? shutdownTimeout = null)
        {
            return new WebSocketResponseQueue(
                webSocket,
                maximumPendingBytes,
                maximumPendingResponses,
                shutdownTimeout ?? TimeSpan.FromSeconds(1),
                NullLogger.Instance);
        }

        private static byte[] BuildExpectedResponse(string text, string type)
        {
            return Encoding.UTF8.GetBytes($"{{\"text\":\"{text}\",\"type\":\"{type}\"}}");
        }

        private sealed class ControlledWebSocket : WebSocket
        {
            private readonly Channel<SendRequest> sends = Channel.CreateUnbounded<SendRequest>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader                  = true,
                SingleWriter                  = true
            });
            private readonly bool ignoreCancellation;

            private int activeSends;
            private int maximumConcurrentSends;
            private WebSocketState state = WebSocketState.Open;

            public override WebSocketCloseStatus? CloseStatus => null;
            public override string CloseStatusDescription => null;
            public override WebSocketState State => state;
            public override string SubProtocol => null;
            public int MaximumConcurrentSends => Volatile.Read(ref maximumConcurrentSends);

            public ControlledWebSocket(bool ignoreCancellation = false)
            {
                this.ignoreCancellation = ignoreCancellation;
            }

            public override void Abort()
            {
                state = WebSocketState.Aborted;
            }

            public override Task CloseAsync(
                WebSocketCloseStatus closeStatus,
                string statusDescription,
                CancellationToken cancellationToken)
            {
                state = WebSocketState.Closed;
                return Task.CompletedTask;
            }

            public override Task CloseOutputAsync(
                WebSocketCloseStatus closeStatus,
                string statusDescription,
                CancellationToken cancellationToken)
            {
                state = WebSocketState.CloseSent;
                return Task.CompletedTask;
            }

            public override void Dispose()
            {
                if (state != WebSocketState.Aborted)
                    state = WebSocketState.Closed;
            }

            public override Task<WebSocketReceiveResult> ReceiveAsync(
                ArraySegment<byte> buffer,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task SendAsync(
                ArraySegment<byte> buffer,
                WebSocketMessageType messageType,
                bool endOfMessage,
                CancellationToken cancellationToken)
            {
                int active = Interlocked.Increment(ref activeSends);
                UpdateMaximumConcurrentSends(active);

                var request = new SendRequest(buffer.ToArray(), cancellationToken, !ignoreCancellation);
                if (!sends.Writer.TryWrite(request))
                    throw new InvalidOperationException();

                return WaitForCompletionAsync(request);
            }

            public ValueTask<SendRequest> NextSendAsync()
            {
                return sends.Reader.ReadAsync();
            }

            public bool TryGetSend(out SendRequest request)
            {
                return sends.Reader.TryRead(out request);
            }

            private async Task WaitForCompletionAsync(SendRequest request)
            {
                try
                {
                    await request.Completion;
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
            private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenRegistration cancellationRegistration;

            public byte[] Buffer { get; }
            public CancellationToken CancellationToken { get; }
            public Task Completion => completion.Task;

            public SendRequest(byte[] buffer, CancellationToken cancellationToken, bool observeCancellation)
            {
                Buffer            = buffer;
                CancellationToken = cancellationToken;
                if (observeCancellation)
                    cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            }

            public void Complete()
            {
                cancellationRegistration.Dispose();
                completion.TrySetResult();
            }

            public void Fail(Exception exception)
            {
                cancellationRegistration.Dispose();
                completion.TrySetException(exception);
            }
        }
    }
}
