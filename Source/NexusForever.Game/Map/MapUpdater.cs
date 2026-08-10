using NexusForever.Game.Abstract.Map;
using NLog;

namespace NexusForever.Game.Map
{
    /// <summary>
    /// Updates maps independently so a failure in one map cannot suppress updates for its siblings.
    /// </summary>
    public sealed class MapUpdater
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Update every supplied map synchronously or on independent worker tasks.
        /// </summary>
        public void Update(IEnumerable<IMap> maps, double lastTick, bool synchronous)
        {
            ArgumentNullException.ThrowIfNull(maps);

            IMap[] mapArray = maps.ToArray();
            if (synchronous)
            {
                foreach (IMap map in mapArray)
                    UpdateMap(map, lastTick);

                return;
            }

            Task[] tasks = mapArray
                .Select(map => Task.Run(() => UpdateMap(map, lastTick)))
                .ToArray();
            Task.WaitAll(tasks);
        }

        private static void UpdateMap(IMap map, double lastTick)
        {
            try
            {
                map.Update(lastTick);
            }
            catch (Exception exception)
            {
                string worldId = map?.Entry?.Id.ToString() ?? "unknown";
                string mapType = map?.GetType().FullName ?? "unknown";
                log.Error(exception, $"Failed to update map {worldId} ({mapType}).");
            }
        }
    }
}
