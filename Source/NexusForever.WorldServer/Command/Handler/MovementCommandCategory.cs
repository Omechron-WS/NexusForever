using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Entity.Movement.Generator;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Convert;
using NexusForever.WorldServer.Command.Static;

namespace NexusForever.WorldServer.Command.Handler
{
    [Command(Permission.Movement, "A collection of commands to control entity movement.", "movement", "move")]
    [CommandTarget(typeof(IWorldEntity))]
    public class MovementCommandCategory : CommandCategory
    {
        [Command(Permission.MovementSpline, "A collection of commands to control entity spline movement.", "spline")]
        public class MovementSplineCategory : CommandCategory
        {
            private sealed class SplineDraft
            {
                public required WeakReference<IBaseMap> Map { get; init; }
                public List<Vector3> Nodes { get; } = [];
            }

            private static ConditionalWeakTable<IWorldEntity, SplineDraft> entityNodes = new();

            [Command(Permission.MovementSplineAdd, "A position to target entity spine nodes.", "add")]
            public void MovementSplineAddHandler(ICommandContext context)
            {
                var entity = context.GetTargetOrInvoker<IWorldEntity>();
                if (entity?.Map == null || context.Invoker?.Map == null || !ReferenceEquals(entity.Map, context.Invoker.Map))
                {
                    context.SendError("The spline target and invoker must be on the same map.");
                    return;
                }

                Vector3 targetPosition = entity.Position;
                Vector3 invokerPosition = context.Invoker.Position;
                if (!IsFinite(targetPosition) || !IsFinite(invokerPosition))
                {
                    context.SendError("Spline nodes must have finite coordinates.");
                    return;
                }
                if (targetPosition == invokerPosition && !entityNodes.TryGetValue(entity, out _))
                {
                    context.SendError("A spline node must differ from the target's initial position.");
                    return;
                }

                SplineDraft draft = entityNodes.GetValue(entity, key => new SplineDraft
                {
                    Map = new WeakReference<IBaseMap>(key.Map),
                    Nodes = { targetPosition }
                });
                if (!draft.Map.TryGetTarget(out IBaseMap draftMap) || !ReferenceEquals(draftMap, entity.Map))
                {
                    context.SendError("The spline draft belongs to a previous map instance; clear it before continuing.");
                    return;
                }
                if (draft.Nodes.Count >= SplinePathLimits.MaximumLinearSourceNodeCount)
                {
                    context.SendError($"A spline can contain at most {SplinePathLimits.MaximumLinearSourceNodeCount} source nodes.");
                    return;
                }

                if (draft.Nodes[^1] == invokerPosition)
                {
                    context.SendError("A spline node must differ from the previous node.");
                    return;
                }

                draft.Nodes.Add(invokerPosition);
                
                context.SendMessage($"Added spline node X:{invokerPosition.X}, Y:{invokerPosition.Y}, Z:{invokerPosition.Z} to entity {entity.Guid}.");
            }

            [Command(Permission.MovementSplineClear, "Clear all positions from target entity spline nodes.", "clear")]
            public void MovementSplineClearHandler(ICommandContext context)
            {
                var entity = context.GetTargetOrInvoker<IWorldEntity>();
                if (entity == null || !entityNodes.TryGetValue(entity, out SplineDraft draft))
                    return;

                entityNodes.Remove(entity);
                context.SendMessage($"Cleared {draft.Nodes.Count} spline nodes for entity {entity.Guid}.");
            }

            [Command(Permission.MovementSplineLaunch, "Launch spline for target entity with previously defined nodes and optional mode and speed.", "launch")]
            public void MovementSplineLaunchHandler(ICommandContext context,
                [Parameter("Mode to launch the spline.", ParameterFlags.None, typeof(DefinedEnumParameterConverter<SplineMode>))]
                SplineMode? mode,
                [Parameter("Speed to launch the spline.")]
                float? speed)
            {
                mode  ??= SplineMode.OneShot;
                speed ??= 3f;

                var entity = context.GetTargetOrInvoker<IWorldEntity>();
                if (entity == null || !entityNodes.TryGetValue(entity, out SplineDraft draft))
                {
                    context.SendMessage("Selected target entity has no nodes!");
                    return;
                }
                if (!entity.MovementManager.ServerControl)
                {
                    context.SendError("Selected target entity is not server controlled.");
                    return;
                }
                if (entity.Map == null
                    || context.Invoker?.Map == null
                    || !draft.Map.TryGetTarget(out IBaseMap draftMap)
                    || !ReferenceEquals(draftMap, entity.Map)
                    || !ReferenceEquals(entity.Map, context.Invoker.Map))
                {
                    context.SendError("The spline draft no longer belongs to the target's current map.");
                    return;
                }

                SplinePathLimits.ValidateSourcePath(draft.Nodes, SplineType.Linear, mode.Value, speed.Value);
                entity.MovementManager.LaunchSpline(draft.Nodes, SplineType.Linear, mode.Value, speed.Value);
                entityNodes.Remove(entity);

                context.SendMessage($"Launching spline for entity {entity.Guid} with {draft.Nodes.Count} nodes.");
            }

            private static bool IsFinite(Vector3 value)
            {
                return float.IsFinite(value.X)
                    && float.IsFinite(value.Y)
                    && float.IsFinite(value.Z);
            }

            internal static void ResetDraftsForTest()
            {
                entityNodes = new ConditionalWeakTable<IWorldEntity, SplineDraft>();
            }
        }

        [Command(Permission.MovementGenerator, "A collection of commands to control entity generator movement.", "generator")]
        public class MovementGeneratorCategory : CommandCategory
        {
            [Command(Permission.MovementGeneratorDirect, "Launch spline for target entity with nodes defined by the direct movement generator.", "direct")]
            public void MovementGeneratorDirectHandler(ICommandContext context)
            {
                var entity = context.GetTargetOrInvoker<IWorldEntity>();
                var generator = new DirectMovementGenerator
                {
                    Begin = entity.Position,
                    Final = context.Invoker.Position,
                    Map   = entity.Map
                };

                entity.MovementManager.LaunchGenerator(generator, 3f);
            }

            [Command(Permission.MovementGeneratorRandom, "Launch spline for target entity with nodes defined by the random movement generator.", "random")]
            public void MovementGeneratorRandomHandler(ICommandContext context)
            {
                var entity = context.GetTargetOrInvoker<IWorldEntity>();
                var generator = new RandomMovementGenerator
                {
                    Begin = entity.Position,
                    Leash = entity.LeashPosition,
                    Range = entity.LeashRange,
                    Map   = entity.Map
                };

                entity.MovementManager.LaunchGenerator(generator, 3f);
            }
        }
    }
}
