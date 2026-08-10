using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Moq;
using NexusForever.WorldServer.Command;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Web;
using NexusForever.WorldServer.Web.Configuration;
using NexusForever.WorldServer.Web.Middleware;

namespace NexusForever.WorldServer.Tests.Web.Middleware
{
    public class WebSocketMiddlewareTests
    {
        private const string Credential = "unit-test-command-credential";
        private const string AllowedOrigin = "https://admin.example.test";

        [Fact]
        public async Task InvokeAsync_DisabledEndpoint_ReturnsNotFound()
        {
            TestHarness harness = CreateHarness(new WebSocketCommandOptions());

            await harness.InvokeAsync(addCredential: true, addOrigin: true);

            Assert.Equal(StatusCodes.Status404NotFound, harness.HttpContext.Response.StatusCode);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_MissingOrigin_ReturnsForbidden()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions());

            await harness.InvokeAsync(addCredential: true, addOrigin: false);

            Assert.Equal(StatusCodes.Status403Forbidden, harness.HttpContext.Response.StatusCode);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_DisallowedOrigin_ReturnsForbidden()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions());
            harness.HttpContext.Request.Headers[HeaderNames.Origin] = "https://untrusted.example.test";

            await harness.InvokeAsync(addCredential: true, addOrigin: false);

            Assert.Equal(StatusCodes.Status403Forbidden, harness.HttpContext.Response.StatusCode);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_DuplicateOriginHeaders_ReturnsForbidden()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions());
            harness.HttpContext.Request.Headers[HeaderNames.Origin] = new StringValues([AllowedOrigin, AllowedOrigin]);

            await harness.InvokeAsync(addCredential: true, addOrigin: false);

            Assert.Equal(StatusCodes.Status403Forbidden, harness.HttpContext.Response.StatusCode);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_MissingBearerCredential_ReturnsUnauthorised()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions());

            await harness.InvokeAsync(addCredential: false, addOrigin: true);

            Assert.Equal(StatusCodes.Status401Unauthorized, harness.HttpContext.Response.StatusCode);
            Assert.Equal("Bearer", harness.HttpContext.Response.Headers[HeaderNames.WWWAuthenticate]);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_QueryStringCredential_ReturnsUnauthorised()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions());
            harness.HttpContext.Request.QueryString = new QueryString($"?access_token={Credential}");

            await harness.InvokeAsync(addCredential: false, addOrigin: true);

            Assert.Equal(StatusCodes.Status401Unauthorized, harness.HttpContext.Response.StatusCode);
            Assert.Equal("Bearer", harness.HttpContext.Response.Headers[HeaderNames.WWWAuthenticate]);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_IncorrectBearerCredential_ReturnsUnauthorised()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions());
            harness.HttpContext.Request.Headers[HeaderNames.Authorization] = "Bearer incorrect";

            await harness.InvokeAsync(addCredential: false, addOrigin: true);

            Assert.Equal(StatusCodes.Status401Unauthorized, harness.HttpContext.Response.StatusCode);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_MalformedAuthorizationHeader_ReturnsUnauthorised()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions());
            harness.HttpContext.Request.Headers[HeaderNames.Authorization] = "Bearer";

            await harness.InvokeAsync(addCredential: false, addOrigin: true);

            Assert.Equal(StatusCodes.Status401Unauthorized, harness.HttpContext.Response.StatusCode);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_MultipleAuthorizationHeaders_ReturnsUnauthorised()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions());
            string header = $"Bearer {Credential}";
            harness.HttpContext.Request.Headers[HeaderNames.Authorization] = new StringValues([header, header]);

            await harness.InvokeAsync(addCredential: false, addOrigin: true);

            Assert.Equal(StatusCodes.Status401Unauthorized, harness.HttpContext.Response.StatusCode);
            Assert.False(harness.WebSocketFeature.Accepted);
        }

        [Fact]
        public async Task InvokeAsync_ValidFragmentedTextAtLimit_DispatchesCommand()
        {
            const string command = "status";
            byte[] payload = Encoding.UTF8.GetBytes($"{{\"message\":\"{command}\"}}");
            WebSocketCommandOptions options = CreateEnabledOptions(payload.Length);
            TestHarness harness = CreateHarness(options,
                ReceiveFrame.Text(payload[..5], false),
                ReceiveFrame.Text(payload[5..], true),
                ReceiveFrame.Close());

            await harness.InvokeAsync(addCredential: true, addOrigin: true);

            Assert.True(harness.WebSocketFeature.Accepted);
            harness.CommandManager.Verify(manager => manager.HandleCommandDelay(harness.CommandContext.Object, command), Times.Once);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, harness.WebSocket.SentCloseStatus);
        }

        [Fact]
        public async Task InvokeAsync_BinaryMessage_ClosesWithInvalidMessageType()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions(), ReceiveFrame.Binary([1, 2, 3]));

            await harness.InvokeAsync(addCredential: true, addOrigin: true);

            Assert.Equal(WebSocketCloseStatus.InvalidMessageType, harness.WebSocket.SentCloseStatus);
            harness.CommandManager.Verify(manager => manager.HandleCommandDelay(It.IsAny<ICommandContext>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_FragmentedMessageOverLimit_ClosesWithMessageTooBig()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions(8),
                ReceiveFrame.Text(Encoding.UTF8.GetBytes("12345"), false),
                ReceiveFrame.Text(Encoding.UTF8.GetBytes("6789"), true));

            await harness.InvokeAsync(addCredential: true, addOrigin: true);

            Assert.Equal(WebSocketCloseStatus.MessageTooBig, harness.WebSocket.SentCloseStatus);
            harness.CommandManager.Verify(manager => manager.HandleCommandDelay(It.IsAny<ICommandContext>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("null")]
        [InlineData("{\"message\":null}")]
        [InlineData("{\"message\":\" \"}")]
        public async Task InvokeAsync_MalformedOrNullCommand_ClosesWithInvalidPayload(string payload)
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions(), ReceiveFrame.Text(Encoding.UTF8.GetBytes(payload), true));

            await harness.InvokeAsync(addCredential: true, addOrigin: true);

            Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, harness.WebSocket.SentCloseStatus);
            harness.CommandManager.Verify(manager => manager.HandleCommandDelay(It.IsAny<ICommandContext>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_InvalidUtf8_ClosesWithInvalidPayload()
        {
            TestHarness harness = CreateHarness(CreateEnabledOptions(), ReceiveFrame.Text([0xC3, 0x28], true));

            await harness.InvokeAsync(addCredential: true, addOrigin: true);

            Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, harness.WebSocket.SentCloseStatus);
            harness.CommandManager.Verify(manager => manager.HandleCommandDelay(It.IsAny<ICommandContext>(), It.IsAny<string>()), Times.Never);
        }

        private static WebSocketCommandOptions CreateEnabledOptions(int maximumMessageSize = 1024)
        {
            return new WebSocketCommandOptions
            {
                Enabled = true,
                CredentialSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Credential))),
                AllowedOrigins = [AllowedOrigin],
                MaximumMessageSize = maximumMessageSize
            };
        }

        private static TestHarness CreateHarness(WebSocketCommandOptions options, params ReceiveFrame[] frames)
        {
            return new TestHarness(options, frames);
        }

        private sealed class TestHarness
        {
            public DefaultHttpContext HttpContext { get; } = new();
            public Mock<ICommandManager> CommandManager { get; } = new();
            public Mock<ICommandContext> CommandContext { get; } = new();
            public TestWebSocket WebSocket { get; }
            public TestWebSocketFeature WebSocketFeature { get; }

            private readonly WebSocketMiddleware middleware;

            public TestHarness(WebSocketCommandOptions options, IEnumerable<ReceiveFrame> frames)
            {
                WebSocket = new TestWebSocket(frames);
                WebSocketFeature = new TestWebSocketFeature(WebSocket);
                HttpContext.Features.Set<IHttpWebSocketFeature>(WebSocketFeature);
                HttpContext.Request.Path = "/ws/commands";

                var commandContextFactory = new Mock<IWebSocketCommandContextFactory>();
                commandContextFactory.Setup(factory => factory.Create(WebSocket)).Returns(CommandContext.Object);

                middleware = new WebSocketMiddleware(
                    _ => Task.CompletedTask,
                    CommandManager.Object,
                    commandContextFactory.Object,
                    Options.Create(options),
                    NullLogger<WebSocketMiddleware>.Instance);
            }

            public Task InvokeAsync(bool addCredential, bool addOrigin)
            {
                if (addCredential)
                    HttpContext.Request.Headers[HeaderNames.Authorization] = $"Bearer {Credential}";
                if (addOrigin)
                    HttpContext.Request.Headers[HeaderNames.Origin] = AllowedOrigin;

                return middleware.InvokeAsync(HttpContext);
            }
        }

        private sealed class TestWebSocketFeature : IHttpWebSocketFeature
        {
            private readonly WebSocket webSocket;

            public bool IsWebSocketRequest => true;
            public bool Accepted { get; private set; }

            public TestWebSocketFeature(WebSocket webSocket)
            {
                this.webSocket = webSocket;
            }

            public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
            {
                Accepted = true;
                return Task.FromResult(webSocket);
            }
        }

        private sealed class TestWebSocket : WebSocket
        {
            private readonly Queue<ReceiveFrame> frames;
            private WebSocketState state = WebSocketState.Open;
            private WebSocketCloseStatus? closeStatus;
            private string closeStatusDescription;

            public override WebSocketCloseStatus? CloseStatus => closeStatus;
            public override string CloseStatusDescription => closeStatusDescription;
            public override WebSocketState State => state;
            public override string SubProtocol => null;

            public WebSocketCloseStatus? SentCloseStatus { get; private set; }

            public TestWebSocket(IEnumerable<ReceiveFrame> frames)
            {
                this.frames = new Queue<ReceiveFrame>(frames);
            }

            public override void Abort()
            {
                state = WebSocketState.Aborted;
            }

            public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription,
                CancellationToken cancellationToken)
            {
                SentCloseStatus = closeStatus;
                state = WebSocketState.Closed;
                return Task.CompletedTask;
            }

            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription,
                CancellationToken cancellationToken)
            {
                SentCloseStatus = closeStatus;
                state = state == WebSocketState.CloseReceived ? WebSocketState.Closed : WebSocketState.CloseSent;
                return Task.CompletedTask;
            }

            public override void Dispose()
            {
                if (state != WebSocketState.Aborted)
                    state = WebSocketState.Closed;
            }

            public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                ReceiveFrame frame = frames.Dequeue();
                Array.Copy(frame.Payload, 0, buffer.Array, buffer.Offset, frame.Payload.Length);
                if (frame.MessageType == WebSocketMessageType.Close)
                {
                    closeStatus = frame.CloseStatus;
                    closeStatusDescription = frame.CloseStatusDescription;
                    state = WebSocketState.CloseReceived;
                }

                return Task.FromResult(new WebSocketReceiveResult(
                    frame.Payload.Length,
                    frame.MessageType,
                    frame.EndOfMessage,
                    frame.CloseStatus,
                    frame.CloseStatusDescription));
            }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
                bool endOfMessage, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        private sealed record ReceiveFrame(
            byte[] Payload,
            WebSocketMessageType MessageType,
            bool EndOfMessage,
            WebSocketCloseStatus? CloseStatus = null,
            string CloseStatusDescription = null)
        {
            public static ReceiveFrame Text(byte[] payload, bool endOfMessage)
            {
                return new ReceiveFrame(payload, WebSocketMessageType.Text, endOfMessage);
            }

            public static ReceiveFrame Binary(byte[] payload)
            {
                return new ReceiveFrame(payload, WebSocketMessageType.Binary, true);
            }

            public static ReceiveFrame Close()
            {
                return new ReceiveFrame([], WebSocketMessageType.Close, true,
                    WebSocketCloseStatus.NormalClosure, "Test complete.");
            }
        }
    }
}
