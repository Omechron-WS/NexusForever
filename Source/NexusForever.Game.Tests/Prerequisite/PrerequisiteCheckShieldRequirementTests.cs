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
    public class PrerequisiteCheckShieldRequirementTests
    {
        [Fact]
        public void AddGamePrerequisiteDiscoversAbsoluteShieldCheck()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.Shield216);

            Assert.IsType<PrerequisiteCheckShieldRequirement>(check);
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, 50u, 50u, true)]
        [InlineData(PrerequisiteComparison.Equal, 49u, 50u, false)]
        [InlineData(PrerequisiteComparison.NotEqual, 49u, 50u, true)]
        [InlineData(PrerequisiteComparison.NotEqual, 50u, 50u, false)]
        [InlineData(PrerequisiteComparison.GreaterThan, 51u, 50u, true)]
        [InlineData(PrerequisiteComparison.GreaterThan, 50u, 50u, false)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 50u, 50u, true)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 49u, 50u, false)]
        [InlineData(PrerequisiteComparison.LessThan, 49u, 50u, true)]
        [InlineData(PrerequisiteComparison.LessThan, 50u, 50u, false)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 50u, 50u, true)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 51u, 50u, false)]
        [InlineData(PrerequisiteComparison.Equal, uint.MaxValue, uint.MaxValue, true)]
        public void MeetsAndTryMeetsCompareUnsignedShieldAndIgnoreObject(
            PrerequisiteComparison comparison,
            uint currentShield,
            uint threshold,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(entity => entity.Shield).Returns(currentShield);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(entity => entity.Shield).Returns(currentShield);
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
            unit.VerifyGet(entity => entity.Shield, Times.Once);
            player.VerifyNoOtherCalls();
            unit.VerifyNoOtherCalls();
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

        private static PrerequisiteCheckShieldRequirement CreateCheck()
        {
            return new PrerequisiteCheckShieldRequirement(
                Mock.Of<ILogger<PrerequisiteCheckShieldRequirement>>());
        }
    }
}
