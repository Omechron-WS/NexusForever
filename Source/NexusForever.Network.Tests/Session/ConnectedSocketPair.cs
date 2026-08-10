using System.Net;
using System.Net.Sockets;

namespace NexusForever.Network.Tests.Session
{
    internal sealed class ConnectedSocketPair : IDisposable
    {
        public Socket Client { get; }
        public Socket Server { get; }

        private ConnectedSocketPair(Socket client, Socket server)
        {
            Client = client;
            Server = server;
        }

        public static async Task<ConnectedSocketPair> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
                Socket server = await accept;
                return new ConnectedSocketPair(client, server);
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }
}
