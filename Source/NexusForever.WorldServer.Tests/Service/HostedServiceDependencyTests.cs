using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexusForever.Game.Abstract.Chat.Format;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Abstract.Matching.Match;
using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Script;
using NexusForever.Shared;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Service;
using Moq;

namespace NexusForever.WorldServer.Tests.Service
{
    public class HostedServiceDependencyTests
    {
        [Fact]
        public void ServiceProvider_ResolvesHostedServiceWithLootManager()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new Mock<IScriptManager>().Object);
            services.AddSingleton(new Mock<ILoginQueueManager>().Object);
            services.AddSingleton(new Mock<INetworkManager<IWorldSession>>().Object);
            services.AddSingleton(new Mock<IMessageManager>().Object);
            services.AddSingleton(new Mock<IMatchingManager>().Object);
            services.AddSingleton(new Mock<IMatchManager>().Object);
            services.AddSingleton(new Mock<IPublicEventTemplateManager>().Object);
            services.AddSingleton(new Mock<IChatFormatManager>().Object);
            services.AddSingleton(new Mock<IGlobalLootManager>().Object);
            services.AddSingleton(new Mock<IWorldManager>().Object);
            services.AddSingleton<HostedService>();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetRequiredService<HostedService>());
        }
    }
}
