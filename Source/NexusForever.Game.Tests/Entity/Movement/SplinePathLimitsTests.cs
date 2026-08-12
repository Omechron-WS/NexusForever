using System.Numerics;
using NexusForever.Game.Static.Entity.Movement.Spline;

namespace NexusForever.Game.Tests.Entity.Movement
{
    public sealed class SplinePathLimitsTests
    {
        [Fact]
        public void LinearPath_ExactSourceLimitIsAccepted()
        {
            List<Vector3> nodes = Enumerable.Range(0, SplinePathLimits.MaximumLinearSourceNodeCount)
                .Select(index => new Vector3(index, 0f, 0f))
                .ToList();

            SplinePathLimits.ValidateSourcePath(nodes, SplineType.Linear, SplineMode.OneShot, 3f);
        }

        [Fact]
        public void LinearPath_AboveSourceLimitIsRejected()
        {
            List<Vector3> nodes = Enumerable.Range(0, SplinePathLimits.MaximumLinearSourceNodeCount + 1)
                .Select(index => new Vector3(index, 0f, 0f))
                .ToList();

            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                nodes,
                SplineType.Linear,
                SplineMode.OneShot,
                3f));
        }

        [Fact]
        public void CatmullRomPath_OnlyControlPointsDifferIsRejected()
        {
            List<Vector3> nodes =
            [
                new Vector3(-1f, 0f, 0f),
                Vector3.Zero,
                Vector3.Zero,
                new Vector3(1f, 0f, 0f)
            ];

            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                nodes,
                SplineType.CatmullRom,
                SplineMode.OneShot,
                3f));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void Path_InvalidSpeedIsRejected(float speed)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, Vector3.One],
                SplineType.Linear,
                SplineMode.OneShot,
                speed));
        }

        [Fact]
        public void Path_PackedFloatSpeedBoundariesAreExact()
        {
            SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, Vector3.One],
                SplineType.Linear,
                SplineMode.OneShot,
                SplinePathLimits.MinimumPackedFloatSpeed);
            SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, Vector3.One],
                SplineType.Linear,
                SplineMode.OneShot,
                SplinePathLimits.MaximumPackedFloatSpeed);

            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, Vector3.One],
                SplineType.Linear,
                SplineMode.OneShot,
                MathF.BitDecrement(SplinePathLimits.MinimumPackedFloatSpeed)));
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, Vector3.One],
                SplineType.Linear,
                SplineMode.OneShot,
                MathF.BitIncrement(SplinePathLimits.MaximumPackedFloatSpeed)));
        }

        [Fact]
        public void Path_DuplicateTravelledSegmentIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, Vector3.One, Vector3.One, new Vector3(2f)],
                SplineType.Linear,
                SplineMode.OneShot,
                3f));
        }

        [Fact]
        public void Path_FloatDistanceOverflowIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [new Vector3(-float.MaxValue, 0f, 0f), new Vector3(float.MaxValue, 0f, 0f)],
                SplineType.Linear,
                SplineMode.OneShot,
                3f));
        }

        [Fact]
        public void Path_FloatAccumulatedLengthOverflowIsRejected()
        {
            const float segment = 1.5e38f;
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, new Vector3(segment, 0f, 0f), Vector3.Zero, new Vector3(segment, 0f, 0f)],
                SplineType.Linear,
                SplineMode.OneShot,
                3f));
        }

        [Fact]
        public void Path_UnknownTypeOrUnsupportedModeIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, Vector3.One],
                (SplineType)2,
                SplineMode.OneShot,
                3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [Vector3.Zero, Vector3.One],
                SplineType.Linear,
                SplineMode.SplineMode6,
                3f));
        }

        [Fact]
        public void CatmullRom_DerivedArithmeticOverflowIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [
                    new Vector3(-1e20f, 0f, 0f),
                    new Vector3(-1f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(1e20f, 0f, 0f)
                ],
                SplineType.CatmullRom,
                SplineMode.OneShot,
                3f));
        }

        [Fact]
        public void CatmullRom_PointComponentsWithoutHermiteHeadroomAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SplinePathLimits.ValidateSourcePath(
                [
                    new Vector3(float.MaxValue, -1f, 0f),
                    new Vector3(float.MaxValue, 0f, 0f),
                    new Vector3(float.MaxValue, 1f, 0f),
                    new Vector3(float.MaxValue, 2f, 0f)
                ],
                SplineType.CatmullRom,
                SplineMode.OneShot,
                3f));
        }
    }
}
