using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Map;
using NexusForever.Game.Static.Setting;

namespace NexusForever.Game.Tests.Map
{
    public class BaseMapDifficultyTests
    {
        [Fact]
        public void NewMapDefaultsToNormalDifficulty()
        {
            var map = new BaseMap(
                Mock.Of<IEntityFactory>(),
                Mock.Of<IPublicEventManager>());

            Assert.Equal(WorldDifficulty.Normal, map.Difficulty);
        }
    }
}
