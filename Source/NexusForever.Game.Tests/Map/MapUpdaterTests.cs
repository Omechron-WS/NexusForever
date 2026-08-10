using Moq;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Map;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Tests.Map
{
    public class MapUpdaterTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Update_MapFailure_DoesNotSuppressSiblingMaps(bool synchronous)
        {
            const double lastTick = 0.25d;
            Mock<IMap> firstMap = CreateMap(1u);
            Mock<IMap> failedMap = CreateMap(2u);
            failedMap.Setup(map => map.Update(lastTick)).Throws(new InvalidOperationException("map failure"));
            Mock<IMap> finalMap = CreateMap(3u);
            var updater = new MapUpdater();

            updater.Update([firstMap.Object, failedMap.Object, finalMap.Object], lastTick, synchronous);

            firstMap.Verify(map => map.Update(lastTick), Times.Once);
            failedMap.Verify(map => map.Update(lastTick), Times.Once);
            finalMap.Verify(map => map.Update(lastTick), Times.Once);
        }

        private static Mock<IMap> CreateMap(uint worldId)
        {
            var map = new Mock<IMap>();
            map.SetupGet(mock => mock.Entry).Returns(new WorldEntry
            {
                Id = worldId
            });
            return map;
        }
    }
}
