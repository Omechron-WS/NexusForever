using Microsoft.Extensions.Logging;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Tests.Prerequisite
{
    public sealed class PrerequisiteComparisonTests
    {
        [Theory]
        [InlineData(1, PrerequisiteComparison.Equal)]
        [InlineData(2, PrerequisiteComparison.NotEqual)]
        [InlineData(3, PrerequisiteComparison.GreaterThan)]
        [InlineData(4, PrerequisiteComparison.GreaterThanOrEqual)]
        [InlineData(5, PrerequisiteComparison.LessThan)]
        [InlineData(6, PrerequisiteComparison.LessThanOrEqual)]
        public void RawComparisonId_MapsToBuild16042Operation(
            int rawComparisonId,
            PrerequisiteComparison expected)
        {
            Assert.Equal(expected, (PrerequisiteComparison)rawComparisonId);
        }

        [Theory]
        [InlineData(3, 10u, 10u, false)]
        [InlineData(3, 11u, 10u, true)]
        [InlineData(4, 10u, 10u, true)]
        [InlineData(4, 9u, 10u, false)]
        [InlineData(5, 10u, 10u, false)]
        [InlineData(5, 9u, 10u, true)]
        [InlineData(6, 10u, 10u, true)]
        [InlineData(6, 11u, 10u, false)]
        public void RawOrderingId_IsSharedByEveryNumericPrerequisiteCheck(
            int rawComparisonId,
            uint current,
            uint threshold,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(unit => unit.Level).Returns(current);
            player.SetupGet(unit => unit.Health).Returns(current);
            player.SetupGet(unit => unit.MaxHealth).Returns(100u);
            player.SetupGet(unit => unit.Shield).Returns(current);
            player.SetupGet(unit => unit.MaxShieldCapacity).Returns(100u);
            player.Setup(unit => unit.TryGetVitalValue(
                    Vital.Resource1,
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                {
                    value = current;
                    return vital == Vital.Resource1;
                }));

            var comparison = (PrerequisiteComparison)rawComparisonId;
            var level = new PrerequisiteCheckLevel(Mock.Of<ILogger<PrerequisiteCheckLevel>>());
            var vital = new PrerequisiteCheckVital(Mock.Of<ILogger<PrerequisiteCheckVital>>());
            var healthPercentage = new PrerequisiteCheckHealth(
                Mock.Of<ILogger<PrerequisiteCheckHealth>>());
            var health = new PrerequisiteCheckHealthRequirement(
                Mock.Of<ILogger<PrerequisiteCheckHealthRequirement>>());
            var shieldPercentage = new PrerequisiteCheckShield(
                Mock.Of<ILogger<PrerequisiteCheckShield>>());
            var shield = new PrerequisiteCheckShieldRequirement(
                Mock.Of<ILogger<PrerequisiteCheckShieldRequirement>>());

            Assert.Equal(expected, level.Meets(player.Object, comparison, threshold, 0u, null));
            Assert.True(level.TryMeets(
                player.Object,
                comparison,
                threshold,
                0u,
                out bool levelMeets));
            Assert.Equal(expected, levelMeets);

            Assert.Equal(expected, vital.Meets(
                player.Object,
                comparison,
                threshold,
                (uint)Vital.Resource1,
                null));
            Assert.True(vital.TryMeets(
                player.Object,
                comparison,
                threshold,
                (uint)Vital.Resource1,
                out bool vitalMeets));
            Assert.Equal(expected, vitalMeets);

            Assert.Equal(expected, healthPercentage.Meets(
                player.Object,
                comparison,
                threshold,
                0u,
                null));
            Assert.True(healthPercentage.TryMeets(
                player.Object,
                comparison,
                threshold,
                0u,
                out bool healthPercentageMeets));
            Assert.Equal(expected, healthPercentageMeets);

            Assert.Equal(expected, health.Meets(player.Object, comparison, threshold, 0u, null));
            Assert.True(health.TryMeets(
                player.Object,
                comparison,
                threshold,
                0u,
                out bool healthMeets));
            Assert.Equal(expected, healthMeets);

            Assert.Equal(expected, shieldPercentage.Meets(
                player.Object,
                comparison,
                threshold,
                uint.MaxValue,
                null));
            Assert.True(shieldPercentage.TryMeets(
                player.Object,
                comparison,
                threshold,
                uint.MaxValue,
                out bool shieldPercentageMeets));
            Assert.Equal(expected, shieldPercentageMeets);

            Assert.Equal(expected, shield.Meets(
                player.Object,
                comparison,
                threshold,
                uint.MaxValue,
                null));
            Assert.True(shield.TryMeets(
                player.Object,
                comparison,
                threshold,
                uint.MaxValue,
                out bool shieldMeets));
            Assert.Equal(expected, shieldMeets);
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
    }
}
