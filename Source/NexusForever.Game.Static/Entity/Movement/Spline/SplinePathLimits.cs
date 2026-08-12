using System.Numerics;

namespace NexusForever.Game.Static.Entity.Movement.Spline
{
    /// <summary>
    /// Build-16042 limits and deterministic validation for server-authored position paths.
    /// </summary>
    public static class SplinePathLimits
    {
        public const int EncodedPositionCount = 1_023;
        public const int LinearPaddingCount = 2;
        public const int MaximumLinearSourceNodeCount = EncodedPositionCount - LinearPaddingCount;
        public const int MaximumGeneratedIntermediateNodeCount = MaximumLinearSourceNodeCount - 2;
        public static readonly float MinimumPackedFloatSpeed = BitConverter.Int32BitsToSingle(0x33800000);
        public static readonly float MaximumPackedFloatSpeed = BitConverter.Int32BitsToSingle(0x47FFEFFF);

        private const float MaximumCatmullComponentMagnitude = float.MaxValue / 8f;

        /// <summary>
        /// Validate a complete source path before any movement state is mutated.
        /// </summary>
        public static void ValidateSourcePath(
            IReadOnlyList<Vector3> nodes,
            SplineType type,
            SplineMode mode,
            float speed)
        {
            ArgumentNullException.ThrowIfNull(nodes);

            // WritePackedFloat encodes positive magnitudes below/above these exact bit
            // thresholds as zero/a small saturated value rather than the requested speed.
            if (!float.IsFinite(speed)
                || speed < MinimumPackedFloatSpeed
                || speed > MaximumPackedFloatSpeed)
                throw new ArgumentOutOfRangeException(nameof(speed));

            if (mode is < SplineMode.OneShot or > SplineMode.CyclicReverse)
                throw new ArgumentOutOfRangeException(nameof(mode));

            (int minimumNodeCount, int maximumNodeCount, int firstTravelledIndex, int finalTravelledIndex) = type switch
            {
                SplineType.Linear     => (2, MaximumLinearSourceNodeCount, 0, nodes.Count - 1),
                SplineType.CatmullRom => (4, EncodedPositionCount, 1, nodes.Count - 2),
                _                     => throw new ArgumentOutOfRangeException(nameof(type))
            };

            if (nodes.Count < minimumNodeCount || nodes.Count > maximumNodeCount)
                throw new ArgumentOutOfRangeException(nameof(nodes));

            foreach (Vector3 node in nodes)
                if (!IsFinite(node))
                    throw new ArgumentOutOfRangeException(nameof(nodes));

            float length = 0f;
            for (int i = firstTravelledIndex; i < finalTravelledIndex; i++)
            {
                float segmentLength = Vector3.Distance(nodes[i], nodes[i + 1]);
                if (!float.IsFinite(segmentLength) || segmentLength <= 0f)
                    throw new ArgumentOutOfRangeException(nameof(nodes));

                length += segmentLength;
                if (!float.IsFinite(length))
                    throw new ArgumentOutOfRangeException(nameof(nodes));
            }

            if (length <= 0f)
                throw new ArgumentOutOfRangeException(nameof(nodes));

            float duration = length / speed;
            if (!float.IsFinite(duration) || duration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(speed));

            float frameScale = duration / length;
            float inverseDuration = 1f / duration;
            if (!float.IsFinite(frameScale) || !float.IsFinite(inverseDuration))
                throw new ArgumentOutOfRangeException(nameof(speed));

            if (type == SplineType.CatmullRom)
                ValidateCatmullRomArithmetic(nodes);
        }

        /// <summary>
        /// Validate an already encoded position path before its first bit is written.
        /// </summary>
        public static void ValidateEncodedPath(
            IReadOnlyList<Vector3> positions,
            SplineType type,
            SplineMode mode,
            float speed)
        {
            ArgumentNullException.ThrowIfNull(positions);
            if (positions.Count > EncodedPositionCount)
                throw new ArgumentOutOfRangeException(nameof(positions));

            IReadOnlyList<Vector3> sourceNodes = type switch
            {
                SplineType.Linear when positions.Count >= 4
                    => positions.Skip(1).Take(positions.Count - LinearPaddingCount).ToArray(),
                SplineType.Linear
                    => throw new ArgumentOutOfRangeException(nameof(positions)),
                SplineType.CatmullRom
                    => positions,
                _
                    => throw new ArgumentOutOfRangeException(nameof(type))
            };

            ValidateSourcePath(sourceNodes, type, mode, speed);

            if (type == SplineType.Linear
                && (positions[0] != positions[1] || positions[^1] != positions[^2]))
                throw new ArgumentOutOfRangeException(nameof(positions));
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.X)
                && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
        }

        private static void ValidateCatmullRomArithmetic(IReadOnlyList<Vector3> nodes)
        {
            // Catmull-Rom's Hermite derivative can combine point coefficients with an
            // absolute sum of 3 and tangent coefficients with an absolute sum of 2.
            // Keeping every input component within one eighth of float.MaxValue bounds
            // every ordered interpolation/derivative sum below five eighths for all t.
            foreach (Vector3 node in nodes)
                if (!HasCatmullComponentHeadroom(node))
                    throw new ArgumentOutOfRangeException(nameof(nodes));

            for (int i = 1; i < nodes.Count - 1; i++)
            {
                Vector3 distanceLeft = Vector3.Subtract(nodes[i], nodes[i - 1]);
                Vector3 distanceRight = Vector3.Subtract(nodes[i + 1], nodes[i]);
                float dotLeft = Vector3.Dot(distanceLeft, distanceLeft);
                float dotRight = Vector3.Dot(distanceRight, distanceRight);
                if (!IsFinite(distanceLeft)
                    || !IsFinite(distanceRight)
                    || !float.IsFinite(dotLeft)
                    || !float.IsFinite(dotRight))
                    throw new ArgumentOutOfRangeException(nameof(nodes));

                if (dotLeft > 0.000099999997f && dotRight > 0.000099999997f)
                {
                    Vector3 combined = (dotRight * distanceLeft) + (dotLeft * distanceRight);
                    Vector3 squared = combined * combined;
                    float vectorSum = squared.X + squared.Y + squared.Z;
                    if (!IsFinite(combined)
                        || !IsFinite(squared)
                        || !float.IsFinite(vectorSum)
                        || vectorSum <= 0f)
                        throw new ArgumentOutOfRangeException(nameof(nodes));

                    float reciprocalRoot = MathF.ReciprocalSqrtEstimate(vectorSum);
                    float scale = MathF.Max(
                        (3f - (vectorSum * reciprocalRoot * reciprocalRoot)) * 0.5f * reciprocalRoot,
                        0f);
                    distanceLeft = scale * combined * MathF.Sqrt(dotLeft);
                    distanceRight = scale * combined * MathF.Sqrt(dotRight);
                    if (!float.IsFinite(reciprocalRoot)
                        || !float.IsFinite(scale)
                        || !IsFinite(distanceLeft)
                        || !IsFinite(distanceRight))
                        throw new ArgumentOutOfRangeException(nameof(nodes));
                }

                if (!HasCatmullComponentHeadroom(distanceLeft)
                    || !HasCatmullComponentHeadroom(distanceRight))
                    throw new ArgumentOutOfRangeException(nameof(nodes));
            }
        }

        private static bool HasCatmullComponentHeadroom(Vector3 value)
        {
            return MathF.Abs(value.X) <= MaximumCatmullComponentMagnitude
                && MathF.Abs(value.Y) <= MaximumCatmullComponentMagnitude
                && MathF.Abs(value.Z) <= MaximumCatmullComponentMagnitude;
        }
    }
}
