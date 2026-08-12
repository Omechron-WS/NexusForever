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
    public class PrerequisiteCheckShieldTests
    {
        [Fact]
        public void AddGamePrerequisiteDiscoversShieldPercentageCheck()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.Shield215);

            Assert.IsType<PrerequisiteCheckShield>(check);
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
        [InlineData(PrerequisiteComparison.Equal, 1_006_573u, 16_776_216u, 6u, true)]
        public void MeetsAndTryMeetsUseClientFloat32OperationOrderAndIgnoreObject(
            PrerequisiteComparison comparison,
            uint currentShield,
            uint maximumShield,
            uint threshold,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(entity => entity.Shield).Returns(currentShield);
            player.SetupGet(entity => entity.MaxShieldCapacity).Returns(maximumShield);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(entity => entity.Shield).Returns(currentShield);
            unit.SetupGet(entity => entity.MaxShieldCapacity).Returns(maximumShield);
            var check = CreateCheck();

            bool playerMeets = check.Meets(
                player.Object,
                comparison,
                threshold,
                uint.MaxValue,
                null);
            bool evaluated = check.TryMeets(
                unit.Object,
                comparison,
                threshold,
                uint.MaxValue,
                out bool unitMeets);

            Assert.Equal(expected, playerMeets);
            Assert.True(evaluated);
            Assert.Equal(expected, unitMeets);
            player.VerifyGet(entity => entity.Shield, Times.Once);
            player.VerifyGet(entity => entity.MaxShieldCapacity, Times.Once);
            unit.VerifyGet(entity => entity.Shield, Times.Once);
            unit.VerifyGet(entity => entity.MaxShieldCapacity, Times.Once);
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
        public void ZeroMaximumShieldMatchesClientUnorderedAndInfinityResults(
            PrerequisiteComparison comparison,
            uint currentShield,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(entity => entity.Shield).Returns(currentShield);
            player.SetupGet(entity => entity.MaxShieldCapacity).Returns(0u);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(entity => entity.Shield).Returns(currentShield);
            unit.SetupGet(entity => entity.MaxShieldCapacity).Returns(0u);
            var check = CreateCheck();

            Assert.Equal(expected, check.Meets(player.Object, comparison, 50u, 3u, null));
            Assert.True(check.TryMeets(unit.Object, comparison, 50u, 3u, out bool meets));
            Assert.Equal(expected, meets);
        }

        [Fact]
        public void FullUnsignedThresholdAndArbitraryObjectAreValidShapes()
        {
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(entity => entity.Shield).Returns(uint.MaxValue);
            unit.SetupGet(entity => entity.MaxShieldCapacity).Returns(uint.MaxValue);
            var check = CreateCheck();

            Assert.True(check.CanEvaluate(
                PrerequisiteComparison.LessThan,
                uint.MaxValue,
                uint.MaxValue));
            Assert.True(check.TryMeets(
                unit.Object,
                PrerequisiteComparison.LessThan,
                uint.MaxValue,
                uint.MaxValue,
                out bool meets));
            Assert.True(meets);
        }

        [Theory]
        [InlineData((PrerequisiteComparison)0)]
        [InlineData((PrerequisiteComparison)999)]
        public void InvalidComparisonFailsClosedWithoutStateReads(
            PrerequisiteComparison comparison)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            var check = CreateCheck();

            Assert.False(check.CanEvaluate(comparison, uint.MaxValue, uint.MaxValue));
            Assert.False(check.Meets(player.Object, comparison, uint.MaxValue, uint.MaxValue, null));
            Assert.False(check.TryMeets(
                unit.Object,
                comparison,
                uint.MaxValue,
                uint.MaxValue,
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
                uint.MaxValue,
                null));
            Assert.False(check.TryMeets(
                null,
                PrerequisiteComparison.Equal,
                0u,
                uint.MaxValue,
                out bool meets));
            Assert.False(meets);
        }

        private static PrerequisiteCheckShield CreateCheck()
        {
            return new PrerequisiteCheckShield(Mock.Of<ILogger<PrerequisiteCheckShield>>());
        }
    }
}
