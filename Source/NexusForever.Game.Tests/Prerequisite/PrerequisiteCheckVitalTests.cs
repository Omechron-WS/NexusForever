using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Prerequisite;
using Moq;

namespace NexusForever.Game.Tests.Prerequisite
{
    public class PrerequisiteCheckVitalTests
    {
        [Theory]
        [InlineData(PrerequisiteComparison.Equal, 5f, 5u, true)]
        [InlineData(PrerequisiteComparison.Equal, 5.5f, 5u, false)]
        [InlineData(PrerequisiteComparison.NotEqual, 5.5f, 5u, true)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual, 5f, 5u, true)]
        [InlineData(PrerequisiteComparison.GreaterThan, 5.5f, 5u, true)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual, 5f, 5u, true)]
        [InlineData(PrerequisiteComparison.LessThan, 4.5f, 5u, true)]
        public void Meets_UsesCurrentVitalValue(
            PrerequisiteComparison comparison,
            float current,
            uint value,
            bool expected)
        {
            Mock<IPlayer> player = CreatePlayer(Vital.Resource1, current);
            var check = new PrerequisiteCheckVital(Mock.Of<ILogger<PrerequisiteCheckVital>>());

            bool result = check.Meets(
                player.Object,
                comparison,
                value,
                (uint)Vital.Resource1,
                null);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void Meets_SupportedZeroIsNotTreatedAsUnknownVital()
        {
            Mock<IPlayer> player = CreatePlayer(Vital.Focus, 0f);
            var check = new PrerequisiteCheckVital(Mock.Of<ILogger<PrerequisiteCheckVital>>());

            bool result = check.Meets(
                player.Object,
                PrerequisiteComparison.Equal,
                0u,
                (uint)Vital.Focus,
                null);

            Assert.True(result);
        }

        [Theory]
        [InlineData((uint)Vital.Breath, (PrerequisiteComparison)1)]
        [InlineData((uint)Vital.Resource1, (PrerequisiteComparison)999)]
        public void Meets_UnsupportedVitalOrComparisonFailsClosed(
            uint objectId,
            PrerequisiteComparison comparison)
        {
            Mock<IPlayer> player = CreatePlayer(Vital.Resource1, 10f);
            var check = new PrerequisiteCheckVital(Mock.Of<ILogger<PrerequisiteCheckVital>>());

            bool result = check.Meets(player.Object, comparison, 10u, objectId, null);

            Assert.False(result);
        }

        private static Mock<IPlayer> CreatePlayer(Vital supportedVital, float current)
        {
            var player = new Mock<IPlayer>();
            player.Setup(unit => unit.TryGetVitalValue(
                    It.IsAny<Vital>(),
                    out It.Ref<float>.IsAny))
                .Returns(new TryGetVitalValue((Vital vital, out float value) =>
                {
                    value = vital == supportedVital ? current : 0f;
                    return vital == supportedVital;
                }));
            return player;
        }

        private delegate bool TryGetVitalValue(Vital vital, out float value);
    }
}
