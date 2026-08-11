using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Group;

namespace NexusForever.Game.Group
{
    public static class ServiceCollectionExtensions
    {
        public static void AddGameGroup(this IServiceCollection sc)
        {
            sc.AddSingleton<GroupSnapshotCache>();
            sc.AddSingleton<IGroupSnapshotCache>(serviceProvider => serviceProvider.GetRequiredService<GroupSnapshotCache>());
        }
    }
}
