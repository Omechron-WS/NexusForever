using System.Numerics;
using NexusForever.Game.Abstract.Entity.Movement.Generator;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Static.Entity.Movement.Spline;

namespace NexusForever.Game.Entity.Movement.Generator
{
    public class RandomMovementGenerator : IRandomMovementGenerator
    {
        private const float StepSize = 2f;

        public Vector3 Begin { get; set; }
        public Vector3 Leash { get; set; }
        public float Range { get; set; }
        public IBaseMap Map { get; set; }

        public List<Vector3> CalculatePath()
        {
            var points = new List<Vector3> { Begin };

            Vector3 final = Leash.GetRandomPoint2D(Range);
            final.Y = Map.GetTerrainHeight(final.X, final.Z) ?? 0f;

            double deltaX = (double)final.X - Begin.X;
            double deltaZ = (double)final.Z - Begin.Z;
            double horizontalDistance = Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
            double segmentCount = Math.Ceiling(horizontalDistance / StepSize);
            int boundedIntermediatePointCount = double.IsFinite(segmentCount)
                && segmentCount <= SplinePathLimits.MaximumGeneratedIntermediateNodeCount + 1
                ? Math.Max(0, (int)segmentCount - 1)
                : 0;
            float angle = (float)Math.Atan2(deltaZ, deltaX);
            for (int i = 0; i < boundedIntermediatePointCount; i++)
            {
                Vector3 next = points[^1].GetPoint2D(angle, StepSize);
                next.Y = Map.GetTerrainHeight(next.X, next.Z) ?? 0f;
                points.Add(next);
            }

            points.Add(final);
            return points;
        }
    }
}
