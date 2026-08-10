using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using NexusForever.WorldServer.Command;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Web.Configuration;

namespace NexusForever.WorldServer.Web.Middleware
{
    /// <summary>
    /// Handles authenticated administrative command WebSocket connections.
    /// </summary>
    public class WebSocketMiddleware
    {
        private const string CommandPath = "/ws/commands";

        private static readonly UTF8Encoding strictUtf8 = new(false, true);

        private readonly RequestDelegate next;
        private readonly ICommandManager commandManager;
        private readonly IWebSocketCommandContextFactory commandContextFactory;
        private readonly ILogger<WebSocketMiddleware> log;
        private readonly bool enabled;
        private readonly int maximumMessageSize;
        private readonly int maximumPendingResponseBytes;
        private readonly int maximumPendingResponses;
        private readonly TimeSpan responseShutdownTimeout;
        private readonly byte[] credentialHash;
        private readonly HashSet<string> allowedOrigins = new(StringComparer.OrdinalIgnoreCase);

        private class ClientMessage
        {
            [JsonProperty("message")]
            public string Message { get; set; }
        }

        /// <summary>
        /// Create a new command WebSocket middleware instance.
        /// </summary>
        public WebSocketMiddleware(
            RequestDelegate next,
            ICommandManager commandManager,
            IWebSocketCommandContextFactory commandContextFactory,
            IOptions<WebSocketCommandOptions> options,
            ILogger<WebSocketMiddleware> log)
        {
            this.next                  = next;
            this.commandManager        = commandManager;
            this.commandContextFactory = commandContextFactory;
            this.log                   = log;

            WebSocketCommandOptions value = options.Value;
            enabled                     = value.Enabled;
            maximumMessageSize          = value.MaximumMessageSize;
            maximumPendingResponseBytes = value.MaximumPendingResponseBytes;
            maximumPendingResponses     = value.MaximumPendingResponses;
            responseShutdownTimeout     = TimeSpan.FromSeconds(value.ResponseShutdownTimeoutSeconds);
            credentialHash              = value.HasValidCredentialHash()
                ? Convert.FromHexString(value.CredentialSha256)
                : new byte[SHA256.HashSizeInBytes];

            foreach (string origin in value.AllowedOrigins ?? [])
            {
                if (WebSocketCommandOrigin.TryNormalise(origin, out string normalisedOrigin))
                    allowedOrigins.Add(normalisedOrigin);
            }
        }

        /// <summary>
        /// Process an HTTP request, handling the administrative command route when matched.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path != CommandPath)
            {
                await next(context);
                return;
            }

            if (!enabled)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!IsOriginAllowed(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!IsBearerAuthenticated(context.Request))
            {
                context.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
            using (webSocket)
            {
                var responseQueue = new WebSocketResponseQueue(
                    webSocket,
                    maximumPendingResponseBytes,
                    maximumPendingResponses,
                    responseShutdownTimeout,
                    log);
                try
                {
                    await ProcessMessagesAsync(webSocket, responseQueue, context.RequestAborted);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    webSocket.Abort();
                }
                catch (WebSocketException exception)
                {
                    log.LogDebug(exception, "Command WebSocket connection ended unexpectedly.");
                    webSocket.Abort();
                }
                catch (Exception exception)
                {
                    log.LogError(exception, "Command WebSocket connection failed.");
                    await StopAndCloseSocketAsync(
                        webSocket,
                        responseQueue,
                        WebSocketCloseStatus.InternalServerError,
                        "Command processing failed.",
                        CancellationToken.None);
                }
                finally
                {
                    await responseQueue.StopAsync();
                }
            }
        }

        private async Task ProcessMessagesAsync(
            WebSocket webSocket,
            WebSocketResponseQueue responseQueue,
            CancellationToken cancellationToken)
        {
            ICommandContext commandContext = commandContextFactory.Create(responseQueue);
            while (webSocket.State == WebSocketState.Open)
            {
                string json = await ReceiveTextMessageAsync(webSocket, responseQueue, cancellationToken);
                if (json == null)
                    return;

                ClientMessage clientMessage;
                try
                {
                    clientMessage = JsonConvert.DeserializeObject<ClientMessage>(json);
                }
                catch (JsonException)
                {
                    await StopAndCloseSocketAsync(
                        webSocket,
                        responseQueue,
                        WebSocketCloseStatus.InvalidPayloadData,
                        "Invalid command message.",
                        cancellationToken);
                    return;
                }

                if (string.IsNullOrWhiteSpace(clientMessage?.Message))
                {
                    await StopAndCloseSocketAsync(
                        webSocket,
                        responseQueue,
                        WebSocketCloseStatus.InvalidPayloadData,
                        "Invalid command message.",
                        cancellationToken);
                    return;
                }

                if (!commandManager.HandleCommandDelay(commandContext, clientMessage.Message))
                {
                    await StopAndCloseSocketAsync(
                        webSocket,
                        responseQueue,
                        WebSocketCloseStatus.PolicyViolation,
                        "Command queue is full.",
                        cancellationToken);
                    return;
                }
            }
        }

        private async Task<string> ReceiveTextMessageAsync(
            WebSocket webSocket,
            WebSocketResponseQueue responseQueue,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var message = new MemoryStream(Math.Min(maximumMessageSize, buffer.Length));

            while (true)
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await StopAndCloseSocketAsync(
                        webSocket,
                        responseQueue,
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        cancellationToken);
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    await StopAndCloseSocketAsync(
                        webSocket,
                        responseQueue,
                        WebSocketCloseStatus.InvalidMessageType,
                        "Text messages are required.",
                        cancellationToken);
                    return null;
                }

                if (message.Length + result.Count > maximumMessageSize)
                {
                    await StopAndCloseSocketAsync(
                        webSocket,
                        responseQueue,
                        WebSocketCloseStatus.MessageTooBig,
                        "Command message is too large.",
                        cancellationToken);
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    break;
            }

            try
            {
                return strictUtf8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
            catch (DecoderFallbackException)
            {
                await StopAndCloseSocketAsync(
                    webSocket,
                    responseQueue,
                    WebSocketCloseStatus.InvalidPayloadData,
                    "Command message is not valid UTF-8.",
                    cancellationToken);
                return null;
            }
        }

        private bool IsBearerAuthenticated(HttpRequest request)
        {
            StringValues values = request.Headers[HeaderNames.Authorization];
            if (values.Count != 1
                || !AuthenticationHeaderValue.TryParse(values[0], out AuthenticationHeaderValue authentication)
                || !authentication.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(authentication.Parameter))
                return false;

            byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(authentication.Parameter));
            return CryptographicOperations.FixedTimeEquals(credentialHash, suppliedHash);
        }

        private bool IsOriginAllowed(HttpRequest request)
        {
            StringValues origins = request.Headers[HeaderNames.Origin];
            return origins.Count == 1
                && WebSocketCommandOrigin.TryNormalise(origins[0], out string normalisedOrigin)
                && allowedOrigins.Contains(normalisedOrigin);
        }

        private async Task StopAndCloseSocketAsync(
            WebSocket webSocket,
            WebSocketResponseQueue responseQueue,
            WebSocketCloseStatus status,
            string description,
            CancellationToken cancellationToken)
        {
            await responseQueue.StopAsync();
            await CloseSocketAsync(webSocket, status, description, cancellationToken);
        }

        private async Task CloseSocketAsync(
            WebSocket webSocket,
            WebSocketCloseStatus status,
            string description,
            CancellationToken cancellationToken)
        {
            if (webSocket.State != WebSocketState.Open && webSocket.State != WebSocketState.CloseReceived)
                return;

            try
            {
                await webSocket.CloseOutputAsync(status, description, cancellationToken);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
            {
                log.LogDebug(exception, "Unable to send command WebSocket close frame.");
                webSocket.Abort();
            }
        }
    }
}
