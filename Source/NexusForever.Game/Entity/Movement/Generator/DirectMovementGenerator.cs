using System.Numerics;
using NexusForever.Game.Abstract.Entity.Movement.Generator;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Static.Entity.Movement.Spline;

namespace NexusForever.Game.Entity.Movement.Generator
{
    public class DirectMovementGenerator : IDirectMovementGenerator
    {
        private const float StepSize = 2f;

        public Vector3 Begin { get; set; }
        public Vector3 Final { get; set; }
        public IBaseMap Map { get; set; }

        public List<Vector3> CalculatePath()
        {
            var points = new List<Vector3> { Begin };

            double deltaX = (double)Final.X - Begin.X;
            double deltaZ = (double)Final.Z - Begin.Z;
            double horizontalDistance = Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
            double segmentCount = Math.Ceiling(horizontalDistance / StepSize);
            int intermediatePointCount = double.IsFinite(segmentCount)
                && segmentCount <= SplinePathLimits.MaximumGeneratedIntermediateNodeCount + 1
                ? Math.Max(0, (int)segmentCount - 1)
                : 0;
            float angle = (float)Math.Atan2(deltaZ, deltaX);
            for (int i = 0; i < intermediatePointCount; i++)
            {
                Vector3 next = points[points.Count - 1].GetPoint2D(angle, StepSize);
                next.Y = Map?.GetTerrainHeight(next.X, next.Z) ?? 0f;
                points.Add(next);
            }

            points.Add(Final);
            return points;
        }
    }
}
