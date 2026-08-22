using Microsoft.Extensions.DependencyInjection;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Friendship;
using NexusForever.Server.Friendship.Network.Internal;
using NexusForever.Server.Friendship.Network.Internal.Handler;
using NexusForever.Server.Friendship.Network.Internal.Handler.Friendship;
using Rebus.Handlers;

namespace NexusForever.Server.Friendship.Tests
{
    public sealed class FriendshipIdentityInviteRegistrationTests
    {
        [Fact]
        public void IdentityInviteHandler_IsRegisteredWithTransactionalOutboxPublisher()
        {
            var services = new ServiceCollection();

            services.AddNetworkInternalHandlers();

            Assert.Contains(services, descriptor =>
                descriptor.ServiceType == typeof(IHandleMessages<FriendshipIdentityInviteRequestMessage>));

            var constructor = Assert.Single(typeof(FriendshipIdentityInviteRequestHandler).GetConstructors());
            var publisher = Assert.Single(constructor.GetParameters(), parameter =>
                typeof(IInternalMessagePublisher).IsAssignableFrom(parameter.ParameterType));
            Assert.Equal(typeof(OutboxMessagePublisher), publisher.ParameterType);
        }
    }
}
