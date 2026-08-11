using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Static.Prerequisite;
using Moq;

namespace NexusForever.Game.Tests.Prerequisite
{
    public class PrerequisiteCheckDeadStateTests
    {
        [Fact]
        public void AddGamePrerequisite_DiscoversDeadStateWithoutRegisteringUnderSpell()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.DeadState);

            Assert.IsType<PrerequisiteCheckDeadState>(check);
            Assert.Null(serviceProvider.GetKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.UnderSpell));
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, 0u, true, true)]
        [InlineData(PrerequisiteComparison.Equal, 0u, false, false)]
        [InlineData(PrerequisiteComparison.Equal, 1u, true, false)]
        [InlineData(PrerequisiteComparison.Equal, 1u, false, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 0u, true, false)]
        [InlineData(PrerequisiteComparison.NotEqual, 0u, false, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 1u, true, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 1u, false, false)]
        public void Meets_MapsComparisonAndValueToRequiredPlayerDeathState(
            PrerequisiteComparison comparison,
            uint value,
            bool isAlive,
            bool expected)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(unit => unit.IsAlive).Returns(isAlive);
            var check = new PrerequisiteCheckDeadState(
                Mock.Of<ILogger<PrerequisiteCheckDeadState>>());

            bool result = check.Meets(player.Object, comparison, value, 0u, null);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, 0u, true, true)]
        [InlineData(PrerequisiteComparison.Equal, 0u, false, false)]
        [InlineData(PrerequisiteComparison.Equal, 1u, true, false)]
        [InlineData(PrerequisiteComparison.Equal, 1u, false, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 0u, true, false)]
        [InlineData(PrerequisiteComparison.NotEqual, 0u, false, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 1u, true, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 1u, false, false)]
        public void TryMeets_MapsComparisonAndValueToRequiredUnitDeathState(
            PrerequisiteComparison comparison,
            uint value,
            bool isAlive,
            bool expected)
        {
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.IsAlive).Returns(isAlive);
            var check = new PrerequisiteCheckDeadState(
                Mock.Of<ILogger<PrerequisiteCheckDeadState>>());

            bool evaluated = check.TryMeets(
                unit.Object,
                comparison,
                value,
                0u,
                out bool meets);

            Assert.True(evaluated);
            Assert.Equal(expected, meets);
        }

        [Theory]
        [InlineData(PrerequisiteComparison.GreaterThan, 0u, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 2u, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 0u, 1u)]
        public void InvalidComparisonOrShapeFailsClosed(
            PrerequisiteComparison comparison,
            uint value,
            uint objectId)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            var check = new PrerequisiteCheckDeadState(
                Mock.Of<ILogger<PrerequisiteCheckDeadState>>());

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

        [Fact]
        public void NullUnitFailsClosed()
        {
            var check = new PrerequisiteCheckDeadState(
                Mock.Of<ILogger<PrerequisiteCheckDeadState>>());

            Assert.False(check.TryMeets(
                null,
                PrerequisiteComparison.Equal,
                0u,
                0u,
                out bool meets));
            Assert.False(meets);
        }
    }
}
