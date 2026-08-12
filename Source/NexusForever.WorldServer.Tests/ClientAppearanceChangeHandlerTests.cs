using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Customisation;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Reputation;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Entity.Player;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientAppearanceChangeHandlerTests
    {
        [Fact]
        public void BoneCountBeyondEntityProjectionCapacity_IsRejectedBeforePlayerOrCustomisationAccess()
        {
            var customisationManager = new Mock<ICustomisationManager>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientAppearanceChangeHandler(customisationManager.Object);

            Assert.Throws<InvalidPacketValueException>(() => handler.HandleMessage(
                session.Object,
                CreateMessage(Enumerable.Repeat(1f, 64))));

            session.VerifyNoOtherCalls();
            customisationManager.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void NonFiniteBone_IsRejectedBeforePlayerOrCustomisationAccess(float bone)
        {
            var customisationManager = new Mock<ICustomisationManager>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientAppearanceChangeHandler(customisationManager.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage([bone])));

            session.VerifyNoOtherCalls();
            customisationManager.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(63)]
        public void BoundedFiniteBones_ReachExistingCustomisationValidation(int boneCount)
        {
            var customisationManager = new Mock<ICustomisationManager>(MockBehavior.Strict);
            customisationManager
                .Setup(manager => manager.Validate(
                    Race.Human,
                    Sex.Male,
                    Faction.Exile,
                    It.Is<IList<(uint Label, uint Value)>>(customisations => customisations.Count == 0)))
                .Returns(false);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            player.SetupGet(value => value.Faction1).Returns(Faction.Exile);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);
            var handler = new ClientAppearanceChangeHandler(customisationManager.Object);

            Assert.Throws<InvalidPacketValueException>(() => handler.HandleMessage(
                session.Object,
                CreateMessage(Enumerable.Repeat(1f, boneCount))));

            customisationManager.Verify(manager => manager.Validate(
                Race.Human,
                Sex.Male,
                Faction.Exile,
                It.Is<IList<(uint Label, uint Value)>>(customisations => customisations.Count == 0)), Times.Once);
            session.VerifyGet(value => value.Player, Times.Once);
            player.VerifyGet(value => value.Faction1, Times.Once);
            customisationManager.VerifyNoOtherCalls();
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
        }

        private static ClientCharacterAppearanceChange CreateMessage(IEnumerable<float> bones)
        {
            var message = new ClientCharacterAppearanceChange();
            SetProperty(message, nameof(ClientCharacterAppearanceChange.Race), Race.Human);
            SetProperty(message, nameof(ClientCharacterAppearanceChange.Sex), Sex.Male);
            message.Bones.AddRange(bones);
            return message;
        }

        private static void SetProperty<T>(ClientCharacterAppearanceChange message, string name, T value)
        {
            typeof(ClientCharacterAppearanceChange)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, value);
        }
    }
}
