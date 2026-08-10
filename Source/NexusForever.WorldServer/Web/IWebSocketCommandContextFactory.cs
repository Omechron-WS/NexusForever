using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Web.Configuration;

namespace NexusForever.WorldServer.Web
{
    /// <summary>
    /// Creates command contexts for authenticated command WebSocket connections.
    /// </summary>
    public interface IWebSocketCommandContextFactory
    {
        /// <summary>
        /// Create a command context for the supplied response queue.
        /// </summary>
        ICommandContext Create(IWebSocketResponseQueue responseQueue);
    }

    /// <summary>
    /// Creates command WebSocket contexts from the configured permission allowlist.
    /// </summary>
    public sealed class WebSocketCommandContextFactory : IWebSocketCommandContextFactory
    {
        private readonly ImmutableHashSet<Permission> permissions;

        /// <summary>
        /// Create a command-context factory from the configured permission allowlist.
        /// </summary>
        public WebSocketCommandContextFactory(IOptions<WebSocketCommandOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            WebSocketCommandOptions value = options.Value;
            ArgumentNullException.ThrowIfNull(value);
            permissions = (value.AllowedPermissions ?? []).ToImmutableHashSet();
        }

        /// <inheritdoc />
        public ICommandContext Create(IWebSocketResponseQueue responseQueue)
        {
            return new WebSocketCommandContext(responseQueue, permissions);
        }
    }
}
