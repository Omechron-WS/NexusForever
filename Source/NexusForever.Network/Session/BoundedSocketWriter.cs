using System.Threading.Channels;

namespace NexusForever.Network.Session
{
    internal sealed class BoundedSocketWriter
    {
        private enum WriterState
        {
            Accepting,
            Completing,
            Aborted,
            Completed
        }

        private readonly int maximumPendingBytes;
        private readonly int maximumPendingFrames;
        private readonly ISocketSendAdapter socketSendAdapter;

        private readonly Channel<byte[]> frames;
        private readonly CancellationTokenSource cancellationTokenSource = new();

        private int state;
        private long pendingBytes;
        private int pendingFrames;

        public Task Completion { get; }

        public long PendingBytes => Interlocked.Read(ref pendingBytes);
        public int PendingFrames => Volatile.Read(ref pendingFrames);

        public BoundedSocketWriter(
            ISocketSendAdapter socketSendAdapter,
            int maximumPendingBytes,
            int maximumPendingFrames)
        {
            ArgumentNullException.ThrowIfNull(socketSendAdapter);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPendingBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPendingFrames);

            this.socketSendAdapter    = socketSendAdapter;
            this.maximumPendingBytes  = maximumPendingBytes;
            this.maximumPendingFrames = maximumPendingFrames;

            frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(maximumPendingFrames)
            {
                AllowSynchronousContinuations = false,
                FullMode                      = BoundedChannelFullMode.Wait,
                SingleReader                  = true,
                SingleWriter                  = false
            });

            Completion = RunAsync();
        }

        public bool TryEnqueue(byte[] frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (frame.Length == 0)
                throw new ArgumentException("A network frame cannot be empty.", nameof(frame));

            if ((WriterState)Volatile.Read(ref state) != WriterState.Accepting)
                return false;

            if (!TryReserveFrame())
                return false;

            if (!TryReserveBytes(frame.Length))
            {
                Interlocked.Decrement(ref pendingFrames);
                return false;
            }

            if ((WriterState)Volatile.Read(ref state) == WriterState.Accepting
                && frames.Writer.TryWrite(frame))
                return true;

            Release(frame.Length);
            return false;
        }

        private bool TryReserveFrame()
        {
            while (true)
            {
                int current = Volatile.Read(ref pendingFrames);
                if (current >= maximumPendingFrames)
                    return false;

                if (Interlocked.CompareExchange(ref pendingFrames, current + 1, current) == current)
                    return true;
            }
        }

        public void Complete()
        {
            if (Interlocked.CompareExchange(
                ref state,
                (int)WriterState.Completing,
                (int)WriterState.Accepting) != (int)WriterState.Accepting)
                return;

            frames.Writer.TryComplete();
        }

        public void Abort()
        {
            while (true)
            {
                WriterState current = (WriterState)Volatile.Read(ref state);
                if (current is WriterState.Aborted or WriterState.Completed)
                    return;

                if (Interlocked.CompareExchange(
                    ref state,
                    (int)WriterState.Aborted,
                    (int)current) == (int)current)
                    break;
            }

            frames.Writer.TryComplete();
            cancellationTokenSource.Cancel();
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
                await foreach (byte[] frame in frames.Reader.ReadAllAsync(cancellationTokenSource.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await SendFrameAsync(frame, cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        Release(frame.Length);
                    }
                }
            }
            catch (Exception) when (cancellationTokenSource.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationTokenSource.Token);
            }
            finally
            {
                frames.Writer.TryComplete();
                while (frames.Reader.TryRead(out byte[] frame))
                    Release(frame.Length);

                Volatile.Write(ref state, (int)WriterState.Completed);
            }
        }

        private async Task SendFrameAsync(byte[] frame, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < frame.Length)
            {
                int sent = await socketSendAdapter.SendAsync(frame.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (sent <= 0 || sent > frame.Length - offset)
                    throw new IOException($"Socket sent an invalid byte count of {sent}.");

                offset += sent;
            }
        }

        private void Release(int length)
        {
            Interlocked.Add(ref pendingBytes, -length);
            Interlocked.Decrement(ref pendingFrames);
        }
    }
}
