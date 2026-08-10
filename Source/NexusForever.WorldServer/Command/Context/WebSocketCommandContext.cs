using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static;
using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Web;

namespace NexusForever.WorldServer.Command.Context
{
    public class WebSocketCommandContext : ICommandContext
    {
        public IWorldEntity Invoker { get; }
        public IWorldEntity Target { get; }

        public Language Language { get; } = Language.English;
        public ImmutableHashSet<Permission> Permissions { get; }

        private readonly IWebSocketResponseQueue responseQueue;

        /// <summary>
        /// Create a new <see cref="WebSocketCommandContext"/> with an explicit permission allowlist.
        /// </summary>
        public WebSocketCommandContext(IWebSocketResponseQueue responseQueue, IEnumerable<Permission> permissions)
        {
            ArgumentNullException.ThrowIfNull(responseQueue);
            ArgumentNullException.ThrowIfNull(permissions);

            this.responseQueue = responseQueue;
            Permissions = permissions.ToImmutableHashSet();
        }

        /// <summary>
        /// Send information message containing the supplied string.
        /// </summary>
        public void SendMessage(string message)
        {
            SendWebSocketMessage(message, "info");
        }

        /// <summary>
        /// Send error message containing the supplied string.
        /// </summary>
        public void SendError(string message)
        {
            SendWebSocketMessage(message, "error");
        }

        /// <summary>
        /// Return <see cref="IWorldEntity"/> target, if no target is present return the <see cref="IWorldEntity"/> invoker.
        /// </summary>
        public T GetTargetOrInvoker<T>() where T : IWorldEntity
        {
            return default;
        }

        private void SendWebSocketMessage(string text, string type)
        {
            if (!responseQueue.TryEnqueue(text, type))
                responseQueue.FailClosed();
        }
    }
}
