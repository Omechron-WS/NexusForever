using System.Net.WebSockets;
using NexusForever.WorldServer.Command.Context;

namespace NexusForever.WorldServer.Web
{
    /// <summary>
    /// Creates command contexts for authenticated command WebSocket connections.
    /// </summary>
    public interface IWebSocketCommandContextFactory
    {
        /// <summary>
        /// Create a command context for the supplied WebSocket.
        /// </summary>
        ICommandContext Create(WebSocket webSocket);
    }

    /// <summary>
    /// Creates the default role-based WebSocket command context.
    /// </summary>
    public sealed class WebSocketCommandContextFactory : IWebSocketCommandContextFactory
    {
        /// <inheritdoc />
        public ICommandContext Create(WebSocket webSocket)
        {
            return new WebSocketCommandContext(webSocket);
        }
    }
}
