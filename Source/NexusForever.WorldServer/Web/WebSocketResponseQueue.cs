using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NexusForever.WorldServer.Web.Configuration;

namespace NexusForever.WorldServer.Web
{
    /// <summary>
    /// Provides non-blocking response staging for an administrative command WebSocket.
    /// </summary>
    public interface IWebSocketResponseQueue
    {
        /// <summary>
        /// Attempt to enqueue a command response.
        /// </summary>
        /// <returns><see langword="true"/> when the response was admitted; otherwise, <see langword="false"/>.</returns>
        bool TryEnqueue(string text, string type);

        /// <summary>
        /// Stop the response pump and abort its WebSocket after a response cannot be admitted.
        /// </summary>
        void FailClosed();
    }

    /// <summary>
    /// Sends bounded command responses through one asynchronous WebSocket writer.
    /// </summary>
    internal sealed class WebSocketResponseQueue : IWebSocketResponseQueue
    {
        private enum QueueState
        {
            Accepting,
            Stopping,
            Failed,
            Completed
        }

        private readonly WebSocket webSocket;
        private readonly ILogger log;
        private readonly int maximumPendingBytes;
        private readonly int maximumPendingResponses;
        private readonly TimeSpan shutdownTimeout;

        private readonly Channel<byte[]> responses;
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly object stopGate = new();

        private int state;
        private long pendingBytes;
        private int pendingResponses;
        private Task stopTask;

        internal Task Completion { get; }
        internal long PendingBytes => Interlocked.Read(ref pendingBytes);
        internal int PendingResponses => Volatile.Read(ref pendingResponses);

        /// <summary>
        /// Create a bounded single-writer response queue.
        /// </summary>
        public WebSocketResponseQueue(
            WebSocket webSocket,
            int maximumPendingBytes,
            int maximumPendingResponses,
            TimeSpan shutdownTimeout,
            ILogger log)
        {
            ArgumentNullException.ThrowIfNull(webSocket);
            ArgumentNullException.ThrowIfNull(log);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPendingBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPendingResponses);
            if (shutdownTimeout <= TimeSpan.Zero
                || shutdownTimeout > TimeSpan.FromSeconds(WebSocketCommandOptions.MaximumAllowedResponseShutdownTimeoutSeconds))
                throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));

            this.webSocket               = webSocket;
            this.log                     = log;
            this.maximumPendingBytes     = maximumPendingBytes;
            this.maximumPendingResponses = maximumPendingResponses;
            this.shutdownTimeout         = shutdownTimeout;

            responses = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(maximumPendingResponses)
            {
                AllowSynchronousContinuations = false,
                FullMode                      = BoundedChannelFullMode.Wait,
                SingleReader                  = true,
                SingleWriter                  = false
            });

            Completion = RunAsync();
        }

        /// <inheritdoc />
        public bool TryEnqueue(string text, string type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if ((QueueState)Volatile.Read(ref state) != QueueState.Accepting)
                return false;

            if ((text != null && Encoding.UTF8.GetByteCount(text) > maximumPendingBytes)
                || Encoding.UTF8.GetByteCount(type) > maximumPendingBytes)
                return false;

            string json = JObject.FromObject(new { text, type }).ToString(Formatting.None);
            byte[] response = Encoding.UTF8.GetBytes(json);

            if (!TryReserveResponse())
                return false;

            if (!TryReserveBytes(response.Length))
            {
                Interlocked.Decrement(ref pendingResponses);
                return false;
            }

            if ((QueueState)Volatile.Read(ref state) == QueueState.Accepting
                && responses.Writer.TryWrite(response))
                return true;

            Release(response.Length);
            return false;
        }

        /// <inheritdoc />
        public void FailClosed()
        {
            if (!TryStop(QueueState.Failed))
                return;

            AbortWebSocket();
        }

        /// <summary>
        /// Stop the response pump, cancelling and discarding any pending responses.
        /// </summary>
        internal Task StopAsync()
        {
            lock (stopGate)
                return stopTask ??= StopCoreAsync();
        }

        private async Task StopCoreAsync()
        {
            TryStop(QueueState.Stopping);

            Task completed = await Task.WhenAny(Completion, Task.Delay(shutdownTimeout)).ConfigureAwait(false);
            if (completed != Completion)
            {
                log.LogWarning("Command WebSocket response pump did not stop within its configured timeout; aborting the connection.");
                AbortWebSocket();
                _ = ObserveCompletionAsync();
                return;
            }

            await ObserveCompletionAsync().ConfigureAwait(false);
        }

        private bool TryStop(QueueState requestedState)
        {
            while (true)
            {
                QueueState current = (QueueState)Volatile.Read(ref state);
                if (current != QueueState.Accepting)
                    return false;

                if (Interlocked.CompareExchange(ref state, (int)requestedState, (int)current) == (int)current)
                    break;
            }

            responses.Writer.TryComplete();
            cancellationTokenSource.Cancel();
            return true;
        }

        private bool TryReserveResponse()
        {
            while (true)
            {
                int current = Volatile.Read(ref pendingResponses);
                if (current >= maximumPendingResponses)
                    return false;

                if (Interlocked.CompareExchange(ref pendingResponses, current + 1, current) == current)
                    return true;
            }
        }

        private bool TryReserveBytes(int length)
        {
            while (true)
            {
                long current = Interlocked.Read(ref pendingBytes);
                if (length > maximumPendingBytes - current)
                    return false;

                if (Interlocked.CompareExchange(ref pendingBytes, current + length, current) == current)
                    return true;
            }
        }

        private async Task RunAsync()
        {
            try
            {
                await foreach (byte[] response in responses.Reader.ReadAllAsync(cancellationTokenSource.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await webSocket.SendAsync(
                            new ArraySegment<byte>(response),
                            WebSocketMessageType.Text,
                            true,
                            cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        Release(response.Length);
                    }
                }
            }
            catch (Exception) when (cancellationTokenSource.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationTokenSource.Token);
            }
            catch
            {
                AbortWebSocket();
                throw;
            }
            finally
            {
                responses.Writer.TryComplete();
                while (responses.Reader.TryRead(out byte[] response))
                    Release(response.Length);

                Volatile.Write(ref state, (int)QueueState.Completed);
            }
        }

        private async Task ObserveCompletionAsync()
        {
            try
            {
                await Completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                log.LogDebug(exception, "Command WebSocket response pump failed.");
            }
        }

        private void AbortWebSocket()
        {
            try
            {
                webSocket.Abort();
            }
            catch (Exception exception)
            {
                log.LogDebug(exception, "Unable to abort command WebSocket response pump.");
            }
        }

        private void Release(int length)
        {
            Interlocked.Add(ref pendingBytes, -length);
            Interlocked.Decrement(ref pendingResponses);
        }
    }
}
