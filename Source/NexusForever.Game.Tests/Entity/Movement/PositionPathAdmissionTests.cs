using System.Numerics;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement.AntiTamper;
using NexusForever.Game.Abstract.Entity.Movement.Command.Mode;
using NexusForever.Game.Abstract.Entity.Movement.Command.Move;
using NexusForever.Game.Abstract.Entity.Movement.Command.Platform;
using NexusForever.Game.Abstract.Entity.Movement.Command.Position;
using NexusForever.Game.Abstract.Entity.Movement.Command.Rotation;
using NexusForever.Game.Abstract.Entity.Movement.Command.Scale;
using NexusForever.Game.Abstract.Entity.Movement.Command.State;
using NexusForever.Game.Abstract.Entity.Movement.Command.Time;
using NexusForever.Game.Abstract.Entity.Movement.Command.Velocity;
using NexusForever.Game.Entity.Movement;
using NexusForever.Game.Static.Entity.Movement.Command.State;
using NexusForever.Game.Static.Entity.Movement.Spline;

namespace NexusForever.Game.Tests.Entity.Movement
{
    public sealed class PositionPathAdmissionTests
    {
        [Fact]
        public void LaunchSpline_InvalidPathDoesNotMutateStateMoveRotationOrPosition()
        {
            var position = new Mock<IPositionCommandGroup>();
            var move = new Mock<IMoveCommandGroup>();
            var rotation = new Mock<IRotationCommandGroup>();
            var state = new Mock<IStateCommandGroup>();
            MovementManager manager = CreateMovementManager(position, move, rotation, state);

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.LaunchSpline(
                [Vector3.Zero, Vector3.Zero],
                SplineType.Linear,
                SplineMode.OneShot,
                3f));

            state.Verify(group => group.SetState(It.IsAny<StateFlags>()), Times.Never);
            move.Verify(group => group.SetMoveDefaults(It.IsAny<bool>()), Times.Never);
            rotation.Verify(group => group.SetRotationDefaults(), Times.Never);
            position.Verify(
                group => group.SetPositionPath(
                    It.IsAny<List<Vector3>>(),
                    It.IsAny<SplineType>(),
                    It.IsAny<SplineMode>(),
                    It.IsAny<float>()),
                Times.Never);
        }

        [Fact]
        public void LaunchSpline_ValidPathPreservesEstablishedMutationOrder()
        {
            var operations = new List<string>();
            var position = new Mock<IPositionCommandGroup>();
            var move = new Mock<IMoveCommandGroup>();
            var rotation = new Mock<IRotationCommandGroup>();
            var state = new Mock<IStateCommandGroup>();
            state.Setup(group => group.SetState(StateFlags.Move)).Callback(() => operations.Add("state"));
            move.Setup(group => group.SetMoveDefaults(false)).Callback(() => operations.Add("move"));
            rotation.Setup(group => group.SetRotationDefaults()).Callback(() => operations.Add("rotation"));
            List<Vector3> nodes = [Vector3.Zero, Vector3.One];
            position
                .Setup(group => group.SetPositionPath(nodes, SplineType.Linear, SplineMode.OneShot, 3f))
                .Callback(() => operations.Add("position"));
            MovementManager manager = CreateMovementManager(position, move, rotation, state);

            manager.LaunchSpline(nodes, SplineType.Linear, SplineMode.OneShot, 3f);

            Assert.Equal(["state", "move", "rotation", "position"], operations);
        }

        [Fact]
        public void Follow_DegeneratePathDoesNotMutateStateMoveRotationOrPosition()
        {
            var position = new Mock<IPositionCommandGroup>();
            position.Setup(group => group.GetPosition()).Returns(Vector3.Zero);
            var move = new Mock<IMoveCommandGroup>();
            var rotation = new Mock<IRotationCommandGroup>();
            var state = new Mock<IStateCommandGroup>();
            MovementManager manager = CreateMovementManager(position, move, rotation, state);
            var map = Mock.Of<NexusForever.Game.Abstract.Map.IBaseMap>();
            var target = new Mock<IWorldEntity>();
            target.SetupGet(entity => entity.Guid).Returns(7u);
            target.SetupGet(entity => entity.Position).Returns(Vector3.Zero);
            target.SetupGet(entity => entity.Rotation).Returns(Vector3.Zero);
            target.SetupGet(entity => entity.Map).Returns(map);

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Follow(target.Object, 0f));

            state.Verify(group => group.SetState(It.IsAny<StateFlags>()), Times.Never);
            move.Verify(group => group.SetMoveDefaults(It.IsAny<bool>()), Times.Never);
            rotation.Verify(group => group.SetRotationFaceUnit(It.IsAny<uint>()), Times.Never);
            position.Verify(
                group => group.SetPositionPath(
                    It.IsAny<List<Vector3>>(),
                    It.IsAny<SplineType>(),
                    It.IsAny<SplineMode>(),
                    It.IsAny<float>()),
                Times.Never);
        }

        [Fact]
        public void LaunchSpline_CatmullArithmeticOverflowDoesNotMutateAnyCommandGroup()
        {
            var position = new Mock<IPositionCommandGroup>();
            var move = new Mock<IMoveCommandGroup>();
            var rotation = new Mock<IRotationCommandGroup>();
            var state = new Mock<IStateCommandGroup>();
            MovementManager manager = CreateMovementManager(position, move, rotation, state);
            List<Vector3> nodes =
            [
                new Vector3(float.MaxValue, -1f, 0f),
                new Vector3(float.MaxValue, 0f, 0f),
                new Vector3(float.MaxValue, 1f, 0f),
                new Vector3(float.MaxValue, 2f, 0f)
            ];

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.LaunchSpline(
                nodes,
                SplineType.CatmullRom,
                SplineMode.OneShot,
                3f));

            state.Verify(group => group.SetState(It.IsAny<StateFlags>()), Times.Never);
            move.Verify(group => group.SetMoveDefaults(It.IsAny<bool>()), Times.Never);
            rotation.Verify(group => group.SetRotationDefaults(), Times.Never);
            position.Verify(
                group => group.SetPositionPath(
                    It.IsAny<List<Vector3>>(),
                    It.IsAny<SplineType>(),
                    It.IsAny<SplineMode>(),
                    It.IsAny<float>()),
                Times.Never);
        }

        private static MovementManager CreateMovementManager(
            Mock<IPositionCommandGroup> position,
            Mock<IMoveCommandGroup> move,
            Mock<IRotationCommandGroup> rotation,
            Mock<IStateCommandGroup> state)
        {
            return new MovementManager(
                Mock.Of<ITimeCommandGroup>(),
                Mock.Of<IPlatformCommandGroup>(),
                position.Object,
                Mock.Of<IVelocityCommandGroup>(),
                move.Object,
                rotation.Object,
                Mock.Of<IScaleCommandGroup>(),
                state.Object,
                Mock.Of<IModeCommandGroup>(),
                Mock.Of<IClientMovementCommandValidator>());
        }
    }
}
