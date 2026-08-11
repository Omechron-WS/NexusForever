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
    public class PrerequisiteCheckHealthRequirementTests
    {
        [Fact]
        public void AddGamePrerequisite_DiscoversHealthRequirementIndependentlyFromHealth()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.HealthRequirement);

            Assert.IsType<PrerequisiteCheckHealthRequirement>(check);
            Assert.IsType<PrerequisiteCheckHealth>(
                serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(PrerequisiteType.Health));
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, 0u, 0u, true)]
        [InlineData(PrerequisiteComparison.Equal, 1u, 0u, false)]
        [InlineData(PrerequisiteComparison.NotEqual, 100u, 100u, false)]
        [InlineData(PrerequisiteComparison.NotEqual, 101u, 100u, true)]
        [InlineData(PrerequisiteComparison.GreaterThan, 5_001u, 5_000u, true)]
        [InlineData(PrerequisiteComparison.GreaterThan, 5_000u, 5_000u, false)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 1u, 1u, true)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 0u, 1u, false)]
        [InlineData(PrerequisiteComparison.LessThan, 0u, 1u, true)]
        [InlineData(PrerequisiteComparison.LessThan, 1u, 1u, false)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 5_000u, 5_000u, true)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 5_001u, 5_000u, false)]
        public void MeetsAndTryMeets_CompareCurrentAbsoluteHealth(
            PrerequisiteComparison comparison,
            uint currentHealth,
            uint value,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(unit => unit.Health).Returns(currentHealth);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(entity => entity.Health).Returns(currentHealth);
            var check = new PrerequisiteCheckHealthRequirement(
                Mock.Of<ILogger<PrerequisiteCheckHealthRequirement>>());

            bool playerMeets = check.Meets(
                player.Object,
                comparison,
                value,
                0u,
                null);
            bool evaluated = check.TryMeets(
                unit.Object,
                comparison,
                value,
                0u,
                out bool unitMeets);

            Assert.Equal(expected, playerMeets);
            Assert.True(evaluated);
            Assert.Equal(expected, unitMeets);
            player.VerifyGet(entity => entity.Health, Times.Once);
            unit.VerifyGet(entity => entity.Health, Times.Once);
            player.VerifyNoOtherCalls();
            unit.VerifyNoOtherCalls();
        }

        [Fact]
        public void CanEvaluate_AcceptsAnyUnsignedThreshold()
        {
            var check = new PrerequisiteCheckHealthRequirement(
                Mock.Of<ILogger<PrerequisiteCheckHealthRequirement>>());

            Assert.True(check.CanEvaluate(
                PrerequisiteComparison.Equal,
                uint.MaxValue,
                0u));
        }

        [Theory]
        [InlineData((PrerequisiteComparison)0, 0u)]
        [InlineData((PrerequisiteComparison)999, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 1u)]
        public void InvalidComparisonOrObjectFailsClosedWithoutStateReads(
            PrerequisiteComparison comparison,
            uint objectId)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            var check = new PrerequisiteCheckHealthRequirement(
                Mock.Of<ILogger<PrerequisiteCheckHealthRequirement>>());

            Assert.False(check.CanEvaluate(comparison, 100u, objectId));
            Assert.False(check.Meets(player.Object, comparison, 100u, objectId, null));
            Assert.False(check.TryMeets(
                unit.Object,
                comparison,
                100u,
                objectId,
                out bool meets));
            Assert.False(meets);
            player.VerifyNoOtherCalls();
            unit.VerifyNoOtherCalls();
        }

        [Fact]
        public void NullPlayerAndUnitFailClosed()
        {
            var check = new PrerequisiteCheckHealthRequirement(
                Mock.Of<ILogger<PrerequisiteCheckHealthRequirement>>());

            Assert.False(check.Meets(
                null,
                PrerequisiteComparison.Equal,
                0u,
                0u,
                null));
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
