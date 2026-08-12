using System.Collections;
using System.Numerics;
using Moq;
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
using NexusForever.Network;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Command;

namespace NexusForever.Game.Tests.Entity.Movement
{
    public sealed class ClientEntityCommandAdmissionTests
    {
        public enum DirectVectorCommandKind
        {
            Position,
            Velocity,
            Move,
            Rotation
        }

        public static TheoryData<DirectVectorCommandKind, int, float> NonFiniteDirectVectorComponents
        {
            get
            {
                var data = new TheoryData<DirectVectorCommandKind, int, float>();
                float[] nonFiniteValues = [float.NaN, float.PositiveInfinity, float.NegativeInfinity];

                foreach (DirectVectorCommandKind kind in Enum.GetValues<DirectVectorCommandKind>())
                {
                    for (int component = 0; component < 3; component++)
                    {
                        foreach (float nonFiniteValue in nonFiniteValues)
                            data.Add(kind, component, nonFiniteValue);
                    }
                }

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(NonFiniteDirectVectorComponents))]
        public void ClientControlled_NonFiniteDirectVectorRejectsWholeBatchBeforeAnyCall(
            DirectVectorCommandKind kind,
            int component,
            float nonFiniteValue)
        {
            var fixture = new MovementFixture();
            fixture.EnableClientControl();
            Vector3 invalidVector = CreateVector(component, nonFiniteValue);
            INetworkEntityCommand[] commands =
            [
                CreateCommand(new SetPositionCommand
                {
                    Position = new Vector3(1f, 2f, 3f),
                    Blend = true
                }),
                CreateCommand(CreateDirectVectorCommand(kind, invalidVector, false))
            ];

            Assert.Throws<InvalidPacketValueException>(() => fixture.Manager.HandleClientEntityCommands(commands, 123u));

            fixture.VerifyNoCalls();
        }

        [Fact]
        public void ClientControlled_FiniteDirectVectorsPreserveOrderAndArguments()
        {
            var fixture = new MovementFixture();
            fixture.EnableClientControl();
            var operations = new List<string>();
            var velocity = new Vector3(1f, 2f, 3f);
            var position = new Vector3(4f, 5f, 6f);
            var rotation = new Vector3(7f, 8f, 9f);
            var move = new Vector3(10f, 11f, 12f);
            fixture.Velocity
                .Setup(group => group.SetVelocity(velocity, true))
                .Callback(() => operations.Add("velocity"));
            fixture.Validator
                .Setup(validator => validator.ValidatePosition())
                .Callback(() => operations.Add("validate-position"));
            fixture.Position
                .Setup(group => group.SetPosition(position, false))
                .Callback(() => operations.Add("position"));
            fixture.Rotation
                .Setup(group => group.SetRotation(rotation, true))
                .Callback(() => operations.Add("rotation"));
            fixture.Move
                .Setup(group => group.SetMove(move, false))
                .Callback(() => operations.Add("move"));
            INetworkEntityCommand[] commands =
            [
                CreateCommand(new SetVelocityCommand { Velocity = velocity, Blend = true }),
                CreateCommand(new SetPositionCommand { Position = position, Blend = false }),
                CreateCommand(new SetRotationCommand { Rotation = rotation, Blend = true }),
                CreateCommand(new SetMoveCommand { Move = move, Blend = false })
            ];

            fixture.Manager.HandleClientEntityCommands(commands, 456u);

            Assert.Equal(["velocity", "validate-position", "position", "rotation", "move"], operations);
            fixture.Velocity.Verify(group => group.SetVelocity(velocity, true), Times.Once);
            fixture.Validator.Verify(validator => validator.ValidatePosition(), Times.Once);
            fixture.Position.Verify(group => group.SetPosition(position, false), Times.Once);
            fixture.Rotation.Verify(group => group.SetRotation(rotation, true), Times.Once);
            fixture.Move.Verify(group => group.SetMove(move, false), Times.Once);
            fixture.VerifyNoCalls();
        }

        [Fact]
        public void ServerControlled_ThrowingEnumerableIsNotEnumerated()
        {
            var fixture = new MovementFixture();

            Exception exception = Record.Exception(() => fixture.Manager.HandleClientEntityCommands(
                new ThrowingCommandEnumerable(),
                789u));

            Assert.Null(exception);
            fixture.VerifyNoCalls();
        }

        private static INetworkEntityCommand CreateCommand(IEntityCommandModel model)
        {
            return new NetworkEntityCommand
            {
                Model = model
            };
        }

        private static IEntityCommandModel CreateDirectVectorCommand(
            DirectVectorCommandKind kind,
            Vector3 vector,
            bool blend)
        {
            return kind switch
            {
                DirectVectorCommandKind.Position => new SetPositionCommand { Position = vector, Blend = blend },
                DirectVectorCommandKind.Velocity => new SetVelocityCommand { Velocity = vector, Blend = blend },
                DirectVectorCommandKind.Move     => new SetMoveCommand { Move = vector, Blend = blend },
                DirectVectorCommandKind.Rotation => new SetRotationCommand { Rotation = vector, Blend = blend },
                _                                => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        private static Vector3 CreateVector(int component, float value)
        {
            return component switch
            {
                0 => new Vector3(value, 2f, 3f),
                1 => new Vector3(1f, value, 3f),
                2 => new Vector3(1f, 2f, value),
                _ => throw new ArgumentOutOfRangeException(nameof(component))
            };
        }

        private sealed class ThrowingCommandEnumerable : IEnumerable<INetworkEntityCommand>
        {
            public IEnumerator<INetworkEntityCommand> GetEnumerator()
            {
                throw new InvalidOperationException("The command enumerable must not be enumerated.");
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private sealed class MovementFixture
        {
            public Mock<ITimeCommandGroup> Time { get; } = new();
            public Mock<IPlatformCommandGroup> Platform { get; } = new();
            public Mock<IPositionCommandGroup> Position { get; } = new();
            public Mock<IVelocityCommandGroup> Velocity { get; } = new();
            public Mock<IMoveCommandGroup> Move { get; } = new();
            public Mock<IRotationCommandGroup> Rotation { get; } = new();
            public Mock<IScaleCommandGroup> Scale { get; } = new();
            public Mock<IStateCommandGroup> State { get; } = new();
            public Mock<IModeCommandGroup> Mode { get; } = new();
            public Mock<IClientMovementCommandValidator> Validator { get; } = new();

            public MovementManager Manager { get; }

            public MovementFixture()
            {
                Manager = new MovementManager(
                    Time.Object,
                    Platform.Object,
                    Position.Object,
                    Velocity.Object,
                    Move.Object,
                    Rotation.Object,
                    Scale.Object,
                    State.Object,
                    Mode.Object,
                    Validator.Object);
            }

            public void EnableClientControl()
            {
                Manager.ServerControl = false;
                ClearCalls();
            }

            public void VerifyNoCalls()
            {
                Time.VerifyNoOtherCalls();
                Platform.VerifyNoOtherCalls();
                Position.VerifyNoOtherCalls();
                Velocity.VerifyNoOtherCalls();
                Move.VerifyNoOtherCalls();
                Rotation.VerifyNoOtherCalls();
                Scale.VerifyNoOtherCalls();
                State.VerifyNoOtherCalls();
                Mode.VerifyNoOtherCalls();
                Validator.VerifyNoOtherCalls();
            }

            private void ClearCalls()
            {
                Time.Invocations.Clear();
                Platform.Invocations.Clear();
                Position.Invocations.Clear();
                Velocity.Invocations.Clear();
                Move.Invocations.Clear();
                Rotation.Invocations.Clear();
                Scale.Invocations.Clear();
                State.Invocations.Clear();
                Mode.Invocations.Clear();
                Validator.Invocations.Clear();
            }
        }
    }
}
