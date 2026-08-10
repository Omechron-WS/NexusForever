using System.Net.Sockets;

namespace NexusForever.Network.Session
{
    internal interface ISocketSendAdapter
    {
        ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
    }

    internal sealed class SocketSendAdapter : ISocketSendAdapter
    {
        private readonly Socket socket;

        public SocketSendAdapter(Socket socket)
        {
            this.socket = socket;
        }

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            return socket.SendAsync(buffer, SocketFlags.None, cancellationToken);
        }
    }
}
