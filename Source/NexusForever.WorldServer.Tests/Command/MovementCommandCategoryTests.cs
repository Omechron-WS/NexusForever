using System.Numerics;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Handler;

namespace NexusForever.WorldServer.Tests.Command
{
    public sealed class MovementCommandCategoryTests : IDisposable
    {
        public MovementCommandCategoryTests()
        {
            MovementCommandCategory.MovementSplineCategory.ResetDraftsForTest();
        }

        public void Dispose()
        {
            MovementCommandCategory.MovementSplineCategory.ResetDraftsForTest();
        }

        [Fact]
        public void FailedLaunchRetainsDraftAndDoesNotInvokeMovement()
        {
            Fixture fixture = new();
            var category = new MovementCommandCategory.MovementSplineCategory();
            fixture.InvokerPosition = Vector3.One;
            category.MovementSplineAddHandler(fixture.Context.Object);

            Assert.Throws<ArgumentOutOfRangeException>(() => category.MovementSplineLaunchHandler(
                fixture.Context.Object,
                SplineMode.SplineMode6,
                3f));

            fixture.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.IsAny<List<Vector3>>(),
                    It.IsAny<SplineType>(),
                    It.IsAny<SplineMode>(),
                    It.IsAny<float>()),
                Times.Never);

            category.MovementSplineLaunchHandler(fixture.Context.Object, SplineMode.OneShot, 3f);

            fixture.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.Is<List<Vector3>>(nodes => nodes.SequenceEqual(new[] { Vector3.Zero, Vector3.One })),
                    SplineType.Linear,
                    SplineMode.OneShot,
                    3f),
                Times.Once);
        }

        [Fact]
        public void SuccessfulLaunchClearsExactlyThatEntityDraft()
        {
            Fixture fixture = new();
            var category = new MovementCommandCategory.MovementSplineCategory();
            fixture.InvokerPosition = Vector3.One;
            category.MovementSplineAddHandler(fixture.Context.Object);

            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);
            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);

            fixture.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.IsAny<List<Vector3>>(),
                    SplineType.Linear,
                    SplineMode.OneShot,
                    3f),
                Times.Once);
        }

        [Fact]
        public void SameGuidDifferentEntityDoesNotShareDraft()
        {
            Fixture first = new(7u);
            Fixture second = new(7u);
            var category = new MovementCommandCategory.MovementSplineCategory();
            first.InvokerPosition = Vector3.One;
            category.MovementSplineAddHandler(first.Context.Object);

            category.MovementSplineLaunchHandler(second.Context.Object, null, null);

            first.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.IsAny<List<Vector3>>(),
                    It.IsAny<SplineType>(),
                    It.IsAny<SplineMode>(),
                    It.IsAny<float>()),
                Times.Never);
            second.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.IsAny<List<Vector3>>(),
                    It.IsAny<SplineType>(),
                    It.IsAny<SplineMode>(),
                    It.IsAny<float>()),
                Times.Never);
        }

        [Fact]
        public void MapChangeRejectsLaunchAndRetainsDraftForExplicitClear()
        {
            Fixture fixture = new();
            var category = new MovementCommandCategory.MovementSplineCategory();
            fixture.InvokerPosition = Vector3.One;
            category.MovementSplineAddHandler(fixture.Context.Object);
            fixture.Map = Mock.Of<IBaseMap>();

            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);
            category.MovementSplineClearHandler(fixture.Context.Object);

            fixture.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.IsAny<List<Vector3>>(),
                    It.IsAny<SplineType>(),
                    It.IsAny<SplineMode>(),
                    It.IsAny<float>()),
                Times.Never);
            fixture.Context.Verify(context => context.SendMessage(It.Is<string>(value => value.StartsWith("Cleared "))), Times.Once);
        }

        [Fact]
        public void DuplicateOrNonFiniteAddDoesNotPoisonDraft()
        {
            Fixture fixture = new();
            var category = new MovementCommandCategory.MovementSplineCategory();
            fixture.InvokerPosition = Vector3.Zero;
            category.MovementSplineAddHandler(fixture.Context.Object);
            fixture.InvokerPosition = new Vector3(float.NaN);
            category.MovementSplineAddHandler(fixture.Context.Object);
            fixture.InvokerPosition = Vector3.One;
            category.MovementSplineAddHandler(fixture.Context.Object);

            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);

            fixture.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.Is<List<Vector3>>(nodes => nodes.SequenceEqual(new[] { Vector3.Zero, Vector3.One })),
                    SplineType.Linear,
                    SplineMode.OneShot,
                    3f),
                Times.Once);
        }

        [Fact]
        public void NonFiniteTargetOnFirstAddDoesNotCreateDraft()
        {
            Fixture fixture = new();
            var category = new MovementCommandCategory.MovementSplineCategory();
            fixture.TargetPosition = new Vector3(float.PositiveInfinity);
            fixture.InvokerPosition = Vector3.One;
            category.MovementSplineAddHandler(fixture.Context.Object);
            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);
            fixture.TargetPosition = Vector3.Zero;
            category.MovementSplineAddHandler(fixture.Context.Object);
            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);

            fixture.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.Is<List<Vector3>>(nodes => nodes.SequenceEqual(new[] { Vector3.Zero, Vector3.One })),
                    SplineType.Linear,
                    SplineMode.OneShot,
                    3f),
                Times.Once);
        }

        [Fact]
        public void NonServerControlledOrThrowingLaunchRetainsDraftUntilSuccess()
        {
            Fixture fixture = new();
            var category = new MovementCommandCategory.MovementSplineCategory();
            fixture.InvokerPosition = Vector3.One;
            category.MovementSplineAddHandler(fixture.Context.Object);
            fixture.ServerControl = false;

            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);
            fixture.ServerControl = true;
            fixture.LaunchFailuresRemaining = 1;
            Assert.Throws<InvalidOperationException>(() => category.MovementSplineLaunchHandler(
                fixture.Context.Object,
                null,
                null));
            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);

            fixture.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.IsAny<List<Vector3>>(),
                    SplineType.Linear,
                    SplineMode.OneShot,
                    3f),
                Times.Exactly(2));
        }

        [Fact]
        public void RejectedInitialDuplicateDoesNotCreateDraft()
        {
            Fixture fixture = new();
            var category = new MovementCommandCategory.MovementSplineCategory();

            category.MovementSplineAddHandler(fixture.Context.Object);
            category.MovementSplineLaunchHandler(fixture.Context.Object, null, null);

            fixture.Movement.Verify(
                manager => manager.LaunchSpline(
                    It.IsAny<List<Vector3>>(),
                    It.IsAny<SplineType>(),
                    It.IsAny<SplineMode>(),
                    It.IsAny<float>()),
                Times.Never);
        }

        private sealed class Fixture
        {
            public Mock<IMovementManager> Movement { get; } = new();
            public Mock<IWorldEntity> Target { get; } = new();
            public Mock<IWorldEntity> Invoker { get; } = new();
            public Mock<ICommandContext> Context { get; } = new();

            public IBaseMap Map { get; set; } = Mock.Of<IBaseMap>();
            public Vector3 TargetPosition { get; set; }
            public Vector3 InvokerPosition { get; set; }
            public bool ServerControl { get; set; } = true;
            public int LaunchFailuresRemaining { get; set; }

            public Fixture(uint guid = 1u)
            {
                Movement.SetupGet(manager => manager.ServerControl).Returns(() => ServerControl);
                Movement
                    .Setup(manager => manager.LaunchSpline(
                        It.IsAny<List<Vector3>>(),
                        It.IsAny<SplineType>(),
                        It.IsAny<SplineMode>(),
                        It.IsAny<float>()))
                    .Callback<List<Vector3>, SplineType, SplineMode, float>((_, _, _, _) =>
                    {
                        if (LaunchFailuresRemaining > 0)
                        {
                            LaunchFailuresRemaining--;
                            throw new InvalidOperationException("injected launch failure");
                        }
                    });
                Target.SetupGet(entity => entity.Guid).Returns(guid);
                Target.SetupGet(entity => entity.Position).Returns(() => TargetPosition);
                Target.SetupGet(entity => entity.Map).Returns(() => Map);
                Target.SetupGet(entity => entity.MovementManager).Returns(Movement.Object);
                Invoker.SetupGet(entity => entity.Position).Returns(() => InvokerPosition);
                Invoker.SetupGet(entity => entity.Map).Returns(() => Map);
                Context.SetupGet(context => context.Invoker).Returns(Invoker.Object);
                Context.Setup(context => context.GetTargetOrInvoker<IWorldEntity>()).Returns(Target.Object);
            }
        }
    }
}
