using System.Numerics;
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
using NexusForever.Game.Static.Combat;
using Moq;

namespace NexusForever.Game.Tests.Combat
{
    public class MovementProcTests
    {
        [Fact]
        public void VelocityTransitions_FireMovementProcsOncePerTransition()
        {
            var owner = new Mock<IUnitEntity>();
            MovementManager manager = CreateMovementManager();
            manager.Initialise(owner.Object);

            manager.SetVelocity(Vector3.UnitX, false);
            manager.SetVelocity(Vector3.UnitY, false);
            manager.SetVelocity(Vector3.Zero, false);
            manager.SetVelocity(Vector3.Zero, false);

            owner.Verify(o => o.FireProc(ProcType.BeginMoving), Times.Once);
            owner.Verify(o => o.FireProc(ProcType.StopsMoving), Times.Once);
        }

        [Fact]
        public void SetVelocityDefaults_StopsAnActiveMovementProcState()
        {
            var owner = new Mock<IUnitEntity>();
            MovementManager manager = CreateMovementManager();
            manager.Initialise(owner.Object);
            manager.SetVelocity(Vector3.UnitX, false);

            manager.SetVelocityDefaults();

            owner.Verify(o => o.FireProc(ProcType.StopsMoving), Times.Once);
        }

        private static MovementManager CreateMovementManager()
        {
            return new MovementManager(
                new Mock<ITimeCommandGroup>().Object,
                new Mock<IPlatformCommandGroup>().Object,
                new Mock<IPositionCommandGroup>().Object,
                new Mock<IVelocityCommandGroup>().Object,
                new Mock<IMoveCommandGroup>().Object,
                new Mock<IRotationCommandGroup>().Object,
                new Mock<IScaleCommandGroup>().Object,
                new Mock<IStateCommandGroup>().Object,
                new Mock<IModeCommandGroup>().Object,
                new Mock<IClientMovementCommandValidator>().Object);
        }
    }
}
