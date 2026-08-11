using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Tests.Prerequisite
{
    public class PrerequisiteCheckHealthTests
    {
        [Fact]
        public void AddGamePrerequisite_DiscoversHealthPercentageCheck()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.Health);

            Assert.IsType<PrerequisiteCheckHealth>(check);
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, 50u, 100u, 50u, true)]
        [InlineData(PrerequisiteComparison.Equal, 49u, 100u, 50u, false)]
        [InlineData(PrerequisiteComparison.NotEqual, 49u, 100u, 50u, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 50u, 100u, 50u, false)]
        [InlineData(PrerequisiteComparison.GreaterThan, 51u, 100u, 50u, true)]
        [InlineData(PrerequisiteComparison.GreaterThan, 50u, 100u, 50u, false)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 50u, 100u, 50u, true)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 49u, 100u, 50u, false)]
        [InlineData(PrerequisiteComparison.LessThan, 49u, 100u, 50u, true)]
        [InlineData(PrerequisiteComparison.LessThan, 50u, 100u, 50u, false)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 50u, 100u, 50u, true)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 51u, 100u, 50u, false)]
        [InlineData(PrerequisiteComparison.Equal, 53u, 100u, 53u, true)]
        [InlineData(PrerequisiteComparison.Equal, 1_006_573u, 16_776_216u, 6u, true)]
        public void MeetsAndTryMeets_UseClientFloat32OperationOrder(
            PrerequisiteComparison comparison,
            uint currentHealth,
            uint maximumHealth,
            uint threshold,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(entity => entity.Health).Returns(currentHealth);
            player.SetupGet(entity => entity.MaxHealth).Returns(maximumHealth);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(entity => entity.Health).Returns(currentHealth);
            unit.SetupGet(entity => entity.MaxHealth).Returns(maximumHealth);
            var check = CreateCheck();

            bool playerMeets = check.Meets(player.Object, comparison, threshold, 0u, null);
            bool evaluated = check.TryMeets(
                unit.Object,
                comparison,
                threshold,
                0u,
                out bool unitMeets);

            Assert.Equal(expected, playerMeets);
            Assert.True(evaluated);
            Assert.Equal(expected, unitMeets);
            player.VerifyGet(entity => entity.Health, Times.Once);
            player.VerifyGet(entity => entity.MaxHealth, Times.Once);
            unit.VerifyGet(entity => entity.Health, Times.Once);
            unit.VerifyGet(entity => entity.MaxHealth, Times.Once);
            player.VerifyNoOtherCalls();
            unit.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, 0u, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 0u, false)]
        [InlineData(PrerequisiteComparison.GreaterThan, 0u, false)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 0u, false)]
        [InlineData(PrerequisiteComparison.LessThan, 0u, false)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 0u, false)]
        [InlineData(PrerequisiteComparison.Equal, 1u, false)]
        [InlineData(PrerequisiteComparison.NotEqual, 1u, true)]
        [InlineData(PrerequisiteComparison.GreaterThan, 1u, true)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 1u, true)]
        [InlineData(PrerequisiteComparison.LessThan, 1u, false)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 1u, false)]
        public void ZeroMaximumHealth_MatchesClientUnorderedAndInfinityResults(
            PrerequisiteComparison comparison,
            uint currentHealth,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(entity => entity.Health).Returns(currentHealth);
            player.SetupGet(entity => entity.MaxHealth).Returns(0u);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(entity => entity.Health).Returns(currentHealth);
            unit.SetupGet(entity => entity.MaxHealth).Returns(0u);
            var check = CreateCheck();

            Assert.Equal(expected, check.Meets(player.Object, comparison, 50u, 0u, null));
            Assert.True(check.TryMeets(unit.Object, comparison, 50u, 0u, out bool meets));
            Assert.Equal(expected, meets);
        }

        [Theory]
        [InlineData((PrerequisiteComparison)0, 50u, 0u)]
        [InlineData((PrerequisiteComparison)999, 50u, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 101u, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 50u, 1u)]
        public void InvalidShapeFailsClosedWithoutStateReads(
            PrerequisiteComparison comparison,
            uint value,
            uint objectId)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            var check = CreateCheck();

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
        public void NullPlayerAndUnitFailClosed()
        {
            var check = CreateCheck();

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

        private static PrerequisiteCheckHealth CreateCheck()
        {
            return new PrerequisiteCheckHealth(Mock.Of<ILogger<PrerequisiteCheckHealth>>());
        }
    }
}
