using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Prerequisite.Check;
using NexusForever.Game.Static.Prerequisite;
using Moq;

namespace NexusForever.Game.Tests.Prerequisite
{
    public class PrerequisiteCheckIsPlayerTests
    {
        [Fact]
        public void AddGamePrerequisite_DiscoversIsPlayerWithoutRegisteringUnderSpell()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.IsPlayer);

            Assert.IsType<PrerequisiteCheckIsPlayer>(check);
            Assert.Null(serviceProvider.GetKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.UnderSpell));
        }

        [Theory]
        [InlineData(PrerequisiteComparison.Equal, true)]
        [InlineData(PrerequisiteComparison.NotEqual, false)]
        public void Meets_EvaluatesSuppliedPlayer(
            PrerequisiteComparison comparison,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var check = new PrerequisiteCheckIsPlayer(
                Mock.Of<ILogger<PrerequisiteCheckIsPlayer>>());

            bool meets = check.Meets(player.Object, comparison, 0u, 0u, null);

            Assert.Equal(expected, meets);
            player.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(true, PrerequisiteComparison.Equal, true)]
        [InlineData(true, PrerequisiteComparison.NotEqual, false)]
        [InlineData(false, PrerequisiteComparison.Equal, false)]
        [InlineData(false, PrerequisiteComparison.NotEqual, true)]
        public void TryMeets_EvaluatesRuntimePlayerIdentity(
            bool player,
            PrerequisiteComparison comparison,
            bool expected)
        {
            IUnitEntity unit = player
                ? new Mock<IPlayer>(MockBehavior.Strict).Object
                : new Mock<IUnitEntity>(MockBehavior.Strict).Object;
            var check = new PrerequisiteCheckIsPlayer(
                Mock.Of<ILogger<PrerequisiteCheckIsPlayer>>());

            bool evaluated = check.TryMeets(
                unit,
                comparison,
                0u,
                0u,
                out bool meets);

            Assert.True(evaluated);
            Assert.Equal(expected, meets);
        }

        [Theory]
        [InlineData(PrerequisiteComparison.GreaterThan, 0u, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 1u, 0u)]
        [InlineData(PrerequisiteComparison.Equal, 0u, 1u)]
        public void InvalidComparisonOrPayloadFailsClosed(
            PrerequisiteComparison comparison,
            uint value,
            uint objectId)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            var check = new PrerequisiteCheckIsPlayer(
                Mock.Of<ILogger<PrerequisiteCheckIsPlayer>>());

            Assert.False(check.CanEvaluate(comparison, value, objectId));
            Assert.False(check.Meets(player.Object, comparison, value, objectId, null));
            Assert.False(check.TryMeets(
                unit.Object,
                comparison,
                value,
                objectId,
                out bool meets));
            Assert.False(meets);
            player.VerifyNoOtherCalls();
            unit.VerifyNoOtherCalls();
        }

        [Fact]
        public void NullPlayerAndUnitFailClosed()
        {
            var check = new PrerequisiteCheckIsPlayer(
                Mock.Of<ILogger<PrerequisiteCheckIsPlayer>>());

            Assert.False(check.Meets(
                null,
                PrerequisiteComparison.Equal,
                0u,
                0u,
                null));
            Assert.False(check.TryMeets(
                null,
                PrerequisiteComparison.Equal,
                0u,
                0u,
                out bool meets));
            Assert.False(meets);
        }
    }
}
