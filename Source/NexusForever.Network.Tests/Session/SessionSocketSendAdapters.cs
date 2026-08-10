using System.Threading.Channels;
using NexusForever.Network.Session;

namespace NexusForever.Network.Tests.Session
{
    internal sealed class RecordingSocketSendAdapter : ISocketSendAdapter
    {
        private readonly Channel<byte[]> frames = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader                  = true,
            SingleWriter                  = true
        });

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] frame = buffer.ToArray();
            if (!frames.Writer.TryWrite(frame))
                throw new InvalidOperationException();

            return ValueTask.FromResult(frame.Length);
        }

        public ValueTask<byte[]> NextFrameAsync()
        {
            return frames.Reader.ReadAsync();
        }

        public bool TryGetFrame(out byte[] frame)
        {
            return frames.Reader.TryRead(out frame);
        }
    }

    internal sealed class BlockingSocketSendAdapter : ISocketSendAdapter
    {
        private readonly Channel<BlockingSendRequest> requests = Channel.CreateUnbounded<BlockingSendRequest>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader                  = true,
            SingleWriter                  = true
        });

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            var request = new BlockingSendRequest(buffer.ToArray(), cancellationToken);
            if (!requests.Writer.TryWrite(request))
                throw new InvalidOperationException();

            return new ValueTask<int>(request.Completion);
        }

        public ValueTask<BlockingSendRequest> NextRequestAsync()
        {
            return requests.Reader.ReadAsync();
        }
    }

    internal sealed class BlockingSendRequest
    {
        private readonly TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration cancellationRegistration;

        public byte[] Buffer { get; }
        public CancellationToken CancellationToken { get; }
        public Task<int> Completion => completion.Task;

        public BlockingSendRequest(byte[] buffer, CancellationToken cancellationToken)
        {
            Buffer                   = buffer;
            CancellationToken        = cancellationToken;
            cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        }

        public void Complete(int sent)
        {
            cancellationRegistration.Dispose();
            completion.TrySetResult(sent);
        }
    }
}
