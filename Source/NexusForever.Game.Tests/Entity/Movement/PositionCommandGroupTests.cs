using System.Numerics;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Entity.Movement.Command.Position;
using NexusForever.Game.Abstract.Entity.Movement.Spline;
using NexusForever.Game.Abstract.Entity.Movement.Spline.Template;
using NexusForever.Game.Entity.Movement.Command.Position;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Network.World.Entity.Command;
using NexusForever.Script.Template;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Entity.Movement
{
    public sealed class PositionCommandGroupTests
    {
        [Fact]
        public void SetPositionPath_ReplacementInitialisationFailurePreservesExistingCommand()
        {
            var existing = new Mock<IPositionCommand>();
            var template = new Mock<ISplineTemplatePath>();
            template
                .Setup(value => value.Initialise(
                    It.IsAny<SplineType>(),
                    It.IsAny<List<Vector3>>()))
                .Throws(new InvalidOperationException("injected failure"));
            var replacement = new PositionPathCommand(template.Object, Mock.Of<ISpline>());
            var factory = new Mock<IFactoryInterface<IPositionCommand>>();
            factory.Setup(value => value.Resolve<PositionPathCommand>()).Returns(replacement);
            var group = new PositionCommandGroup(factory.Object);
            typeof(PositionCommandGroup)
                .GetProperty(nameof(PositionCommandGroup.Command))
                ?.SetValue(group, existing.Object);

            Assert.Throws<InvalidOperationException>(() => group.SetPositionPath(
                [Vector3.Zero, Vector3.One],
                SplineType.Linear,
                SplineMode.OneShot,
                3f));

            Assert.Same(existing.Object, group.Command);
            Assert.False(group.IsDirty);
        }

        [Fact]
        public void Update_FinalisedCommandIsCommittedBeforeCallbacksAndReentrantReplacementSurvives()
        {
            var initial = new PositionCommand();
            var completedEndpoint = new PositionCommand();
            var callbackEndpoint = new PositionCommand();
            var callbackReplacement = new PositionCommand();
            var factory = new Mock<IFactoryInterface<IPositionCommand>>();
            factory.SetupSequence(value => value.Resolve<PositionCommand>())
                .Returns(initial)
                .Returns(completedEndpoint)
                .Returns(callbackEndpoint)
                .Returns(callbackReplacement);

            var completed = new Mock<IPositionCommand>(MockBehavior.Strict);
            var completedPosition = new Vector3(1f, 2f, 3f);
            completed.Setup(value => value.Update(0.25d));
            completed.SetupGet(value => value.IsFinalised).Returns(true);
            completed.Setup(value => value.GetPosition()).Returns(completedPosition);

            var firstScript = new Mock<IWorldEntityScript>(MockBehavior.Strict);
            var secondScript = new Mock<IWorldEntityScript>(MockBehavior.Strict);
            var replacementPosition = new Vector3(4f, 5f, 6f);
            var callbackCommands = new List<IPositionCommand>();
            var group = new PositionCommandGroup(factory.Object);
            firstScript
                .Setup(value => value.OnPositionEntityCommandFinalise(completed.Object))
                .Callback(() =>
                {
                    callbackCommands.Add(group.Command);
                    group.SetPosition(replacementPosition, false);
                });
            secondScript
                .Setup(value => value.OnPositionEntityCommandFinalise(completed.Object))
                .Callback(() => callbackCommands.Add(group.Command));

            var owner = new Mock<IWorldEntity>(MockBehavior.Strict);
            owner
                .Setup(value => value.InvokeScriptCollection<IWorldEntityScript>(
                    It.IsAny<Action<IWorldEntityScript>>()))
                .Callback<Action<IWorldEntityScript>>(callback =>
                {
                    callback(firstScript.Object);
                    callback(secondScript.Object);
                });
            var movementManager = new Mock<IMovementManager>(MockBehavior.Strict);
            movementManager.SetupGet(value => value.Owner).Returns(owner.Object);
            movementManager.Setup(value => value.GetMode()).Returns(ModeType.Walk);
            group.Initialise(movementManager.Object);
            typeof(PositionCommandGroup)
                .GetProperty(nameof(PositionCommandGroup.Command))
                ?.SetValue(group, completed.Object);
            group.IsDirty = false;

            group.Update(0.25d);

            Assert.Equal(
                new IPositionCommand[] { completedEndpoint, callbackReplacement },
                callbackCommands);
            Assert.Same(callbackReplacement, group.Command);
            Assert.Equal(replacementPosition, group.GetPosition());
            Assert.True(group.IsDirty);
            var networkModel = Assert.IsType<SetPositionCommand>(
                group.GetNetworkEntityCommand().Model);
            Assert.Equal(replacementPosition, networkModel.Position);
            Assert.False(networkModel.Blend);
            completed.Verify(value => value.Update(0.25d), Times.Once);
            completed.VerifyGet(value => value.IsFinalised, Times.Once);
            completed.Verify(value => value.GetPosition(), Times.Once);
            firstScript.VerifyAll();
            secondScript.VerifyAll();
            owner.VerifyAll();
            movementManager.VerifyAll();
            factory.Verify(value => value.Resolve<PositionCommand>(), Times.Exactly(4));
        }

        [Fact]
        public void Finalise_ReplacementResolutionFailurePreservesExistingCommand()
        {
            var existing = new Mock<IPositionCommand>(MockBehavior.Strict);
            existing.Setup(value => value.GetPosition()).Returns(Vector3.One);
            var factory = new Mock<IFactoryInterface<IPositionCommand>>();
            factory.Setup(value => value.Resolve<PositionCommand>()).Returns((PositionCommand)null);
            var movementManager = new Mock<IMovementManager>(MockBehavior.Strict);
            movementManager.Setup(value => value.GetMode()).Returns(ModeType.Walk);
            var group = new PositionCommandGroup(factory.Object);
            typeof(PositionCommandGroup)
                .GetField("movementManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(group, movementManager.Object);
            typeof(PositionCommandGroup)
                .GetProperty(nameof(PositionCommandGroup.Command))
                ?.SetValue(group, existing.Object);

            Assert.Throws<InvalidOperationException>(() => group.Finalise());

            Assert.Same(existing.Object, group.Command);
            Assert.False(group.IsDirty);
            existing.Verify(value => value.GetPosition(), Times.Once);
            factory.VerifyAll();
            movementManager.VerifyAll();
        }

        [Fact]
        public void Update_ReentrantReplacementDuringCommandUpdateSuppressesStaleCompletion()
        {
            var initial = new PositionCommand();
            var intermediate = new PositionCommand();
            var replacement = new PositionCommand();
            var factory = new Mock<IFactoryInterface<IPositionCommand>>();
            factory.SetupSequence(value => value.Resolve<PositionCommand>())
                .Returns(initial)
                .Returns(intermediate)
                .Returns(replacement);
            var group = new PositionCommandGroup(factory.Object);
            var replacementPosition = new Vector3(7f, 8f, 9f);
            var completed = new Mock<IPositionCommand>(MockBehavior.Strict);
            completed.Setup(value => value.GetPosition()).Returns(Vector3.One);
            completed
                .Setup(value => value.Update(0.25d))
                .Callback(() => group.SetPosition(replacementPosition, false));

            var movementManager = new Mock<IMovementManager>(MockBehavior.Strict);
            movementManager.Setup(value => value.GetMode()).Returns(ModeType.Walk);
            group.Initialise(movementManager.Object);
            typeof(PositionCommandGroup)
                .GetProperty(nameof(PositionCommandGroup.Command))
                ?.SetValue(group, completed.Object);
            group.IsDirty = false;

            group.Update(0.25d);

            Assert.Same(replacement, group.Command);
            Assert.Equal(replacementPosition, group.GetPosition());
            Assert.True(group.IsDirty);
            completed.Verify(value => value.Update(0.25d), Times.Once);
            completed.Verify(value => value.GetPosition(), Times.Once);
            completed.VerifyGet(value => value.IsFinalised, Times.Never);
            movementManager.VerifyAll();
            factory.Verify(value => value.Resolve<PositionCommand>(), Times.Exactly(3));
        }

        [Fact]
        public void Update_CallbackDispatchFailureLeavesCompletedEndpointCommitted()
        {
            var initial = new PositionCommand();
            var completedEndpoint = new PositionCommand();
            var factory = new Mock<IFactoryInterface<IPositionCommand>>();
            factory.SetupSequence(value => value.Resolve<PositionCommand>())
                .Returns(initial)
                .Returns(completedEndpoint);
            var completed = new Mock<IPositionCommand>(MockBehavior.Strict);
            var completedPosition = new Vector3(10f, 11f, 12f);
            completed.Setup(value => value.Update(0.25d));
            completed.SetupGet(value => value.IsFinalised).Returns(true);
            completed.Setup(value => value.GetPosition()).Returns(completedPosition);
            var owner = new Mock<IWorldEntity>(MockBehavior.Strict);
            owner
                .Setup(value => value.InvokeScriptCollection<IWorldEntityScript>(
                    It.IsAny<Action<IWorldEntityScript>>()))
                .Throws(new InvalidOperationException("injected failure"));
            var movementManager = new Mock<IMovementManager>(MockBehavior.Strict);
            movementManager.SetupGet(value => value.Owner).Returns(owner.Object);
            movementManager.Setup(value => value.GetMode()).Returns(ModeType.Walk);
            var group = new PositionCommandGroup(factory.Object);
            group.Initialise(movementManager.Object);
            typeof(PositionCommandGroup)
                .GetProperty(nameof(PositionCommandGroup.Command))
                ?.SetValue(group, completed.Object);
            group.IsDirty = false;

            Assert.Throws<InvalidOperationException>(() => group.Update(0.25d));

            Assert.Same(completedEndpoint, group.Command);
            Assert.Equal(completedPosition, group.GetPosition());
            Assert.True(group.IsDirty);
            completed.Verify(value => value.Update(0.25d), Times.Once);
            completed.VerifyGet(value => value.IsFinalised, Times.Once);
            completed.Verify(value => value.GetPosition(), Times.Once);
            owner.VerifyAll();
            movementManager.VerifyAll();
            factory.Verify(value => value.Resolve<PositionCommand>(), Times.Exactly(2));
        }
    }
}
