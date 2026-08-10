using System;
using System.Collections.Generic;
using NexusForever.Game.Static.RBAC;

namespace NexusForever.WorldServer.Web.Configuration
{
    /// <summary>
    /// Configuration for the administrative command WebSocket endpoint.
    /// </summary>
    public class WebSocketCommandOptions
    {
        /// <summary>
        /// Configuration section containing command WebSocket options.
        /// </summary>
        public const string SectionName = "WebSocketCommands";

        /// <summary>
        /// Maximum configurable complete message size.
        /// </summary>
        public const int MaximumAllowedMessageSize = ushort.MaxValue;

        /// <summary>
        /// Maximum configurable number of delayed commands pending globally.
        /// </summary>
        public const int MaximumAllowedPendingCommands = 16_384;

        /// <summary>
        /// Maximum configurable response bytes pending for one WebSocket connection.
        /// </summary>
        public const int MaximumAllowedPendingResponseBytes = 16 * 1024 * 1024;

        /// <summary>
        /// Maximum configurable response messages pending for one WebSocket connection.
        /// </summary>
        public const int MaximumAllowedPendingResponses = 4_096;

        /// <summary>
        /// Maximum configurable response-pump shutdown timeout.
        /// </summary>
        public const double MaximumAllowedResponseShutdownTimeoutSeconds = 30d;

        private const int Sha256HexLength = 64;

        /// <summary>
        /// Returns whether the command WebSocket endpoint is enabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// SHA-256 digest of the shared administrative bearer credential, encoded as hexadecimal.
        /// </summary>
        /// <remarks>
        /// Supply this through the <c>WebSocketCommands__CredentialSha256</c> environment variable rather than a URL.
        /// </remarks>
        public string CredentialSha256 { get; set; }

        /// <summary>
        /// Exact HTTP or HTTPS origins permitted to open the command WebSocket.
        /// </summary>
        public List<string> AllowedOrigins { get; set; } = [];

        /// <summary>
        /// Exact command permissions granted to authenticated WebSocket connections.
        /// </summary>
        /// <remarks>
        /// The empty default grants no command permissions. Parent category and child command permissions must each be listed.
        /// </remarks>
        public List<Permission> AllowedPermissions { get; set; } = [];

        /// <summary>
        /// Maximum size, in bytes, of one complete client message.
        /// </summary>
        public int MaximumMessageSize { get; set; } = 16 * 1024;

        /// <summary>
        /// Maximum number of delayed commands pending globally.
        /// </summary>
        public int MaximumPendingCommands { get; set; } = 1_024;

        /// <summary>
        /// Maximum number of response bytes queued or being sent for one connection.
        /// </summary>
        public int MaximumPendingResponseBytes { get; set; } = 256 * 1024;

        /// <summary>
        /// Maximum number of response messages queued or being sent for one connection.
        /// </summary>
        public int MaximumPendingResponses { get; set; } = 128;

        /// <summary>
        /// Maximum time allowed for the response pump to stop before its WebSocket is aborted.
        /// </summary>
        public double ResponseShutdownTimeoutSeconds { get; set; } = 2d;

        /// <summary>
        /// Returns whether the configured credential digest is a valid SHA-256 digest.
        /// </summary>
        public bool HasValidCredentialHash()
        {
            if (CredentialSha256?.Length != Sha256HexLength)
                return false;

            try
            {
                return Convert.FromHexString(CredentialSha256).Length == Sha256HexLength / 2;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    internal static class WebSocketCommandOrigin
    {
        public static bool TryNormalise(string origin, out string normalisedOrigin)
        {
            normalisedOrigin = null;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            if (string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
                return false;

            normalisedOrigin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            return true;
        }
    }
}
