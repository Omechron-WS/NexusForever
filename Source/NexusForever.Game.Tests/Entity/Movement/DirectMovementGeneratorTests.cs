using System.Numerics;
using NexusForever.Game.Abstract.Entity.Movement.Spline.Mode;
using NexusForever.Game.Abstract.Entity.Movement.Spline.Type;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Entity.Movement.Generator;
using NexusForever.Game.Entity.Movement.Spline;
using NexusForever.Game.Entity.Movement.Spline.Mode;
using NexusForever.Game.Entity.Movement.Spline.Template;
using NexusForever.Game.Entity.Movement.Spline.Type;
using NexusForever.Game.Static.Entity.Movement.Spline;
using Moq;

namespace NexusForever.Game.Tests.Entity.Movement
{
    public class DirectMovementGeneratorTests
    {
        [Fact]
        public void ExactStepMultiple_ProducesUniqueEndpointAndNaturallyFinalisesLinearSpline()
        {
            var map = new Mock<IBaseMap>();
            map
                .Setup(instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()))
                .Returns(0f);

            var generator = new DirectMovementGenerator
            {
                Begin = Vector3.Zero,
                Final = new Vector3(4f, 0f, 0f),
                Map = map.Object
            };

            List<Vector3> nodes = generator.CalculatePath();

            Assert.Equal(
                new[] { Vector3.Zero, new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f) },
                nodes);
            Assert.NotEqual(nodes[^2], nodes[^1]);

            Vector3 finalPosition = FinaliseLinearSpline(nodes, 2d);

            Assert.True(IsFinite(finalPosition));
            Assert.Equal(generator.Final, finalPosition);
        }

        [Fact]
        public void SlopedTerrain_UsesHorizontalSegmentsWithoutOvershootingAndFinalisesAtExactEndpoint()
        {
            var map = new Mock<IBaseMap>();
            map
                .Setup(instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()))
                .Returns((float x, float _) => 10f + (x * 1.5f));

            var generator = new DirectMovementGenerator
            {
                Begin = new Vector3(0f, 10f, 0f),
                Final = new Vector3(4f, 16f, 0f),
                Map = map.Object
            };

            List<Vector3> nodes = generator.CalculatePath();

            Assert.Equal(
                new[]
                {
                    generator.Begin,
                    new Vector3(2f, 13f, 0f),
                    generator.Final
                },
                nodes);
            map.Verify(
                instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()),
                Times.Once);

            Vector3 finalPosition = FinaliseLinearSpline(nodes, 4d);

            Assert.True(IsFinite(finalPosition));
            Assert.Equal(generator.Final, finalPosition);
        }

        [Fact]
        public void VerticalOnlyPath_DoesNotSampleTerrainAndFinalisesAtExactEndpoint()
        {
            var map = new Mock<IBaseMap>();
            var generator = new DirectMovementGenerator
            {
                Begin = new Vector3(3f, -5f, 7f),
                Final = new Vector3(3f, 15f, 7f),
                Map = map.Object
            };

            List<Vector3> nodes = generator.CalculatePath();

            Assert.Equal(new[] { generator.Begin, generator.Final }, nodes);
            map.Verify(
                instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()),
                Times.Never);

            Vector3 finalPosition = FinaliseLinearSpline(nodes, 10d);

            Assert.True(IsFinite(finalPosition));
            Assert.Equal(generator.Final, finalPosition);
        }

        [Fact]
        public void HugeFinitePath_FailsClosedWithoutUnboundedTerrainSampling()
        {
            var map = new Mock<IBaseMap>();
            var generator = new DirectMovementGenerator
            {
                Begin = Vector3.Zero,
                Final = new Vector3(10_000f, 0f, 0f),
                Map = map.Object
            };

            List<Vector3> nodes = generator.CalculatePath();

            Assert.Equal(new[] { generator.Begin, generator.Final }, nodes);
            map.Verify(
                instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()),
                Times.Never);
        }

        [Fact]
        public void ExactWireBoundary_ProducesMaximumLinearSourceNodes()
        {
            var map = new Mock<IBaseMap>();
            map
                .Setup(instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()))
                .Returns(0f);
            var generator = new DirectMovementGenerator
            {
                Begin = Vector3.Zero,
                Final = new Vector3(2_040f, 0f, 0f),
                Map = map.Object
            };

            List<Vector3> nodes = generator.CalculatePath();

            Assert.Equal(SplinePathLimits.MaximumLinearSourceNodeCount, nodes.Count);
            Assert.Equal(generator.Final, nodes[^1]);
            map.Verify(
                instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()),
                Times.Exactly(SplinePathLimits.MaximumGeneratedIntermediateNodeCount));
        }

        [Fact]
        public void FirstPathAboveWireBoundary_FallsBackToEndpointsWithoutTerrainSampling()
        {
            var map = new Mock<IBaseMap>();
            var generator = new DirectMovementGenerator
            {
                Begin = Vector3.Zero,
                Final = new Vector3(2_041f, 0f, 0f),
                Map = map.Object
            };

            List<Vector3> nodes = generator.CalculatePath();

            Assert.Equal([generator.Begin, generator.Final], nodes);
            map.Verify(
                instance => instance.GetTerrainHeight(It.IsAny<float>(), It.IsAny<float>()),
                Times.Never);
        }

        private static Vector3 FinaliseLinearSpline(List<Vector3> nodes, double elapsedSeconds)
        {
            var typeFactory = new Mock<ISplineTypeFactory>();
            typeFactory
                .Setup(factory => factory.Create(SplineType.Linear))
                .Returns(new SplineTypeLinear());
            var modeFactory = new Mock<ISplineModeFactory>();
            modeFactory
                .Setup(factory => factory.Create(SplineMode.OneShot))
                .Returns(new SplineModeOneShot());

            var template = new SplineTemplatePath();
            template.Initialise(SplineType.Linear, nodes);
            var spline = new Spline(typeFactory.Object, modeFactory.Object);
            spline.Initialise(template, SplineMode.OneShot, 2f);

            spline.Update(elapsedSeconds);

            Assert.True(spline.IsFinialised);
            return spline.GetPosition();
        }

        private static bool IsFinite(Vector3 position)
        {
            return float.IsFinite(position.X)
                && float.IsFinite(position.Y)
                && float.IsFinite(position.Z);
        }
    }
}
