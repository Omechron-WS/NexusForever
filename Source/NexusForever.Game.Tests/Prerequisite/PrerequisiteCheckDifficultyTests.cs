using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Static.Prerequisite;
using NexusForever.Game.Static.Setting;

namespace NexusForever.Game.Tests.Prerequisite
{
    public class PrerequisiteCheckDifficultyTests
    {
        [Fact]
        public void AddGamePrerequisite_DiscoversDifficultyCheck()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.Difficulty);

            Assert.IsType<PrerequisiteCheckDifficulty>(check);
        }

        [Theory]
        [InlineData(WorldDifficulty.Normal, WorldDifficulty.Normal, true)]
        [InlineData(WorldDifficulty.Normal, WorldDifficulty.Veteran, false)]
        [InlineData(WorldDifficulty.Veteran, WorldDifficulty.Normal, false)]
        [InlineData(WorldDifficulty.Veteran, WorldDifficulty.Veteran, true)]
        public void MeetsAndTryMeets_CompareRequiredDifficultyWithMapAuthority(
            WorldDifficulty mapDifficulty,
            WorldDifficulty requiredDifficulty,
            bool expected)
        {
            var map = new Mock<IBaseMap>(MockBehavior.Strict);
            map.SetupGet(value => value.Difficulty).Returns(mapDifficulty);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Map).Returns(map.Object);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(value => value.Map).Returns(map.Object);
            PrerequisiteCheckDifficulty check = CreateCheck();

            bool playerMeets = check.Meets(
                player.Object,
                PrerequisiteComparison.Equal,
                (uint)requiredDifficulty,
                0u,
                null);
            bool evaluated = check.TryMeets(
                unit.Object,
                PrerequisiteComparison.Equal,
                (uint)requiredDifficulty,
                0u,
                out bool unitMeets);

            Assert.Equal(expected, playerMeets);
            Assert.True(evaluated);
            Assert.Equal(expected, unitMeets);
            player.VerifyGet(value => value.Map, Times.Once);
            unit.VerifyGet(value => value.Map, Times.Once);
            map.VerifyGet(value => value.Difficulty, Times.Exactly(2));
            player.VerifyNoOtherCalls();
            unit.VerifyNoOtherCalls();
            map.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(PrerequisiteComparison.NotEqual, (uint)WorldDifficulty.Normal, 0u)]
        [InlineData(PrerequisiteComparison.GreaterThan, (uint)WorldDifficulty.Normal, 0u)]
        [InlineData(PrerequisiteComparison.Equal, (uint)WorldDifficulty.Count, 0u)]
        [InlineData(PrerequisiteComparison.Equal, (uint)WorldDifficulty.Normal, 1u)]
        public void InvalidShapeFailsClosedWithoutMapReads(
            PrerequisiteComparison comparison,
            uint value,
            uint objectId)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            PrerequisiteCheckDifficulty check = CreateCheck();

            Assert.False(check.CanEvaluate(comparison, value, objectId));
            Assert.False(check.Meets(player.Object, comparison, value, objectId, null));
            Assert.False(check.TryMeets(
                unit.Object,
                comparison,
                value,
                objectId,
                out bool meets));
            Assert.False(meets);
            player.VerifyNoOtherCalls();
            unit.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(WorldDifficulty.Count)]
        [InlineData((WorldDifficulty)3)]
        public void InvalidMapAuthorityFailsClosed(WorldDifficulty difficulty)
        {
            var map = new Mock<IBaseMap>(MockBehavior.Strict);
            map.SetupGet(value => value.Difficulty).Returns(difficulty);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Map).Returns(map.Object);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(value => value.Map).Returns(map.Object);
            PrerequisiteCheckDifficulty check = CreateCheck();

            Assert.False(check.Meets(
                player.Object,
                PrerequisiteComparison.Equal,
                (uint)WorldDifficulty.Normal,
                0u,
                null));
            Assert.False(check.TryMeets(
                unit.Object,
                PrerequisiteComparison.Equal,
                (uint)WorldDifficulty.Normal,
                0u,
                out bool meets));
            Assert.False(meets);
        }

        [Fact]
        public void NullSubjectsAndMapsFailClosed()
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Map).Returns((IBaseMap)null);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(value => value.Map).Returns((IBaseMap)null);
            PrerequisiteCheckDifficulty check = CreateCheck();

            Assert.False(check.Meets(
                null,
                PrerequisiteComparison.Equal,
                (uint)WorldDifficulty.Normal,
                0u,
                null));
            Assert.False(check.TryMeets(
                null,
                PrerequisiteComparison.Equal,
                (uint)WorldDifficulty.Normal,
                0u,
                out bool nullMeets));
            Assert.False(nullMeets);
            Assert.False(check.Meets(
                player.Object,
                PrerequisiteComparison.Equal,
                (uint)WorldDifficulty.Normal,
                0u,
                null));
            Assert.False(check.TryMeets(
                unit.Object,
                PrerequisiteComparison.Equal,
                (uint)WorldDifficulty.Normal,
                0u,
                out bool mapMeets));
            Assert.False(mapMeets);
        }

        private static PrerequisiteCheckDifficulty CreateCheck()
        {
            return new PrerequisiteCheckDifficulty(
                Mock.Of<ILogger<PrerequisiteCheckDifficulty>>());
        }
    }
}
