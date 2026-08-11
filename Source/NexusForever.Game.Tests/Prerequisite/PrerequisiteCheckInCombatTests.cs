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
    public class PrerequisiteCheckInCombatTests
    {
        [Fact]
        public void AddGamePrerequisite_DiscoversInCombatWithoutRegisteringUnderSpell()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.InCombat);

            Assert.IsType<PrerequisiteCheckInCombat>(check);
            Assert.Null(serviceProvider.GetKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.UnderSpell));
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, false, false)]
        [InlineData(PrerequisiteComparison.Equal, true, true)]
        [InlineData(PrerequisiteComparison.NotEqual, false, true)]
        [InlineData(PrerequisiteComparison.NotEqual, true, false)]
        public void Meets_MapsComparisonToRequiredPlayerCombatState(
            PrerequisiteComparison comparison,
            bool inCombat,
            bool expected)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(unit => unit.InCombat).Returns(inCombat);
            var check = new PrerequisiteCheckInCombat(
                Mock.Of<ILogger<PrerequisiteCheckInCombat>>());

            bool result = check.Meets(player.Object, comparison, 0u, 0u, null);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, false, false)]
        [InlineData(PrerequisiteComparison.Equal, true, true)]
        [InlineData(PrerequisiteComparison.NotEqual, false, true)]
        [InlineData(PrerequisiteComparison.NotEqual, true, false)]
        public void TryMeets_MapsComparisonToRequiredUnitCombatState(
            PrerequisiteComparison comparison,
            bool inCombat,
            bool expected)
        {
            var unit = new Mock<IUnitEntity>();
            unit.SetupGet(entity => entity.InCombat).Returns(inCombat);
            var check = new PrerequisiteCheckInCombat(
                Mock.Of<ILogger<PrerequisiteCheckInCombat>>());

            bool evaluated = check.TryMeets(
                unit.Object,
                comparison,
                0u,
                0u,
                out bool meets);

            Assert.True(evaluated);
            Assert.Equal(expected, meets);
        }

        [Theory]
        [InlineData(PrerequisiteComparison.GreaterThan, 0u, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 1u, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 0u, 1u)]
        public void InvalidComparisonOrShapeFailsClosed(
            PrerequisiteComparison comparison,
            uint value,
            uint objectId)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            var check = new PrerequisiteCheckInCombat(
                Mock.Of<ILogger<PrerequisiteCheckInCombat>>());

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
    }
}
