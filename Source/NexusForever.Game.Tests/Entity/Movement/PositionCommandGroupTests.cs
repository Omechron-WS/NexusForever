using System.Numerics;
using Moq;
using NexusForever.Game.Abstract.Entity.Movement.Command.Position;
using NexusForever.Game.Abstract.Entity.Movement.Spline;
using NexusForever.Game.Abstract.Entity.Movement.Spline.Template;
using NexusForever.Game.Entity.Movement.Command.Position;
using NexusForever.Game.Static.Entity.Movement.Spline;
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
    }
}
