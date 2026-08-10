using Microsoft.Extensions.Options;
using Moq;
using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Web;
using NexusForever.WorldServer.Web.Configuration;

namespace NexusForever.WorldServer.Tests.Command.Context
{
    public class WebSocketCommandContextTests
    {
        [Fact]
        public void Factory_UsesOnlyConfiguredPermissionAllowlist()
        {
            var factory = new WebSocketCommandContextFactory(Options.Create(new WebSocketCommandOptions
            {
                AllowedPermissions = [Permission.Help, Permission.Realm]
            }));

            ICommandContext context = factory.Create(Mock.Of<IWebSocketResponseQueue>());

            Assert.Equal(2, context.Permissions.Count);
            Assert.Contains(Permission.Help, context.Permissions);
            Assert.Contains(Permission.Realm, context.Permissions);
            Assert.DoesNotContain(Permission.RealmShutdown, context.Permissions);
        }

        [Fact]
        public void Factory_EmptyAllowlist_GrantsNoPermissions()
        {
            var factory = new WebSocketCommandContextFactory(Options.Create(new WebSocketCommandOptions()));

            ICommandContext context = factory.Create(Mock.Of<IWebSocketResponseQueue>());

            Assert.Empty(context.Permissions);
        }

        [Fact]
        public void SendMessage_AdmittedResponse_DoesNotFailConnection()
        {
            var responseQueue = new Mock<IWebSocketResponseQueue>();
            responseQueue.Setup(queue => queue.TryEnqueue("message", "info")).Returns(true);
            var context = new WebSocketCommandContext(responseQueue.Object, []);

            context.SendMessage("message");

            responseQueue.Verify(queue => queue.TryEnqueue("message", "info"), Times.Once);
            responseQueue.Verify(queue => queue.FailClosed(), Times.Never);
        }

        [Fact]
        public void SendError_RejectedResponse_FailsConnectionClosed()
        {
            var responseQueue = new Mock<IWebSocketResponseQueue>();
            responseQueue.Setup(queue => queue.TryEnqueue("error", "error")).Returns(false);
            var context = new WebSocketCommandContext(responseQueue.Object, []);

            context.SendError("error");

            responseQueue.Verify(queue => queue.FailClosed(), Times.Once);
        }
    }
}
