using System.Numerics;
using Moq;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Entity.Movement.Generator;
using NexusForever.Game.Static.Entity.Movement.Spline;

namespace NexusForever.Game.Tests.Entity.Movement
{
    public sealed class RandomMovementGeneratorTests
    {
        [Fact]
        public void ExactWireBoundary_NeverExceedsMaximumLinearSourceNodes()
        {
            var map = new Mock<IBaseMap>();
            map
                .Setup(instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()))
                .Returns(0f);
            var generator = new RandomMovementGenerator
            {
                Begin = Vector3.Zero,
                Leash = new Vector3(2_040f, 0f, 0f),
                Range = 0f,
                Map = map.Object
            };

            List<Vector3> nodes = generator.CalculatePath();

            Assert.Equal(SplinePathLimits.MaximumLinearSourceNodeCount, nodes.Count);
            Assert.Equal(generator.Leash, nodes[^1]);
            Assert.NotEqual(nodes[^2], nodes[^1]);
            map.Verify(
                instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()),
                Times.Exactly(SplinePathLimits.MaximumGeneratedIntermediateNodeCount + 1));
        }

        [Fact]
        public void PathAboveWireBoundary_FallsBackToEndpointsWithoutIntermediateSampling()
        {
            var map = new Mock<IBaseMap>();
            map
                .Setup(instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()))
                .Returns(0f);
            var generator = new RandomMovementGenerator
            {
                Begin = Vector3.Zero,
                Leash = new Vector3(2_041f, 0f, 0f),
                Range = 0f,
                Map = map.Object
            };

            List<Vector3> nodes = generator.CalculatePath();

            Assert.Equal([generator.Begin, generator.Leash], nodes);
            map.Verify(
                instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()),
                Times.Once);
        }
    }
}
