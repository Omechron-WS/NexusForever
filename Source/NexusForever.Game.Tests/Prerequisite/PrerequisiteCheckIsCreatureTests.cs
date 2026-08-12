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
    public class PrerequisiteCheckIsCreatureTests
    {
        [Fact]
        public void AddGamePrerequisite_DiscoversIsCreatureWithoutRegisteringUnderSpell()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGamePrerequisite();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IPrerequisiteCheck check = serviceProvider.GetRequiredKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.IsCreature);

            Assert.IsType<PrerequisiteCheckIsCreature>(check);
            Assert.Null(serviceProvider.GetKeyedService<IPrerequisiteCheck>(
                PrerequisiteType.UnderSpell));
        }

        [Theory]
        [InlineData(6_540u, PrerequisiteComparison.Equal, 6_540u, 2_446u, true)]
        [InlineData(6_540u, PrerequisiteComparison.NotEqual, 6_540u, uint.MaxValue, false)]
        [InlineData(0u, PrerequisiteComparison.Equal, 0u, uint.MaxValue, true)]
        [InlineData(0u, PrerequisiteComparison.NotEqual, uint.MaxValue, 0u, true)]
        public void Meets_EvaluatesPlayerCreatureIdAndIgnoresObjectId(
            uint creatureId,
            PrerequisiteComparison comparison,
            uint value,
            uint objectId,
            bool expected)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(entity => entity.CreatureId).Returns(creatureId);
            var check = CreateCheck();

            bool meets = check.Meets(
                player.Object,
                comparison,
                value,
                objectId,
                null);

            Assert.Equal(expected, meets);
            player.VerifyGet(entity => entity.CreatureId, Times.Once);
            player.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(5_991u, PrerequisiteComparison.Equal, 5_991u, 0u, true)]
        [InlineData(5_991u, PrerequisiteComparison.Equal, 7_471u, 0u, false)]
        [InlineData(7_471u, PrerequisiteComparison.NotEqual, 7_471u, 0u, false)]
        [InlineData(5_991u, PrerequisiteComparison.NotEqual, 7_471u, 0u, true)]
        [InlineData(0u, PrerequisiteComparison.Equal, 0u, 0u, true)]
        [InlineData(uint.MaxValue, PrerequisiteComparison.Equal, uint.MaxValue, 2_446u, true)]
        public void TryMeets_EvaluatesFullUnsignedCreatureIdDomain(
            uint creatureId,
            PrerequisiteComparison comparison,
            uint value,
            uint objectId,
            bool expected)
        {
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            unit.SetupGet(entity => entity.CreatureId).Returns(creatureId);
            var check = CreateCheck();

            bool evaluated = check.TryMeets(
                unit.Object,
                comparison,
                value,
                objectId,
                out bool meets);

            Assert.True(evaluated);
            Assert.Equal(expected, meets);
            unit.VerifyGet(entity => entity.CreatureId, Times.Once);
            unit.VerifyNoOtherCalls();
        }

        [Fact]
        public void CanEvaluate_AcceptsUnsignedValueAndIgnoredObjectId()
        {
            PrerequisiteCheckIsCreature check = CreateCheck();

            Assert.True(check.CanEvaluate(
                PrerequisiteComparison.Equal,
                0u,
                uint.MaxValue));
            Assert.True(check.CanEvaluate(
                PrerequisiteComparison.NotEqual,
                uint.MaxValue,
                2_446u));
        }

        [Theory]
        [InlineData(PrerequisiteComparison.GreaterThan)]
        [InlineData(PrerequisiteComparison.GreaterThanOrEqual)]
        [InlineData(PrerequisiteComparison.LessThan)]
        [InlineData(PrerequisiteComparison.LessThanOrEqual)]
        [InlineData((PrerequisiteComparison)0)]
        [InlineData((PrerequisiteComparison)99)]
        public void UnsupportedComparisonFailsClosedWithoutReadingCreatureId(
            PrerequisiteComparison comparison)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var unit = new Mock<IUnitEntity>(MockBehavior.Strict);
            PrerequisiteCheckIsCreature check = CreateCheck();

            Assert.False(check.CanEvaluate(comparison, uint.MaxValue, uint.MaxValue));
            Assert.False(check.Meets(
                player.Object,
                comparison,
                uint.MaxValue,
                uint.MaxValue,
                null));
            Assert.False(check.TryMeets(
                unit.Object,
                comparison,
                uint.MaxValue,
                uint.MaxValue,
                out bool meets));
            Assert.False(meets);
            player.VerifyNoOtherCalls();
            unit.VerifyNoOtherCalls();
        }

        [Fact]
        public void NullPlayerAndUnitFailClosed()
        {
            PrerequisiteCheckIsCreature check = CreateCheck();

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

        private static PrerequisiteCheckIsCreature CreateCheck()
        {
            return new PrerequisiteCheckIsCreature(
                Mock.Of<ILogger<PrerequisiteCheckIsCreature>>());
        }
    }
}
