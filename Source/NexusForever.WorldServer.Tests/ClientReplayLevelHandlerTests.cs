using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Entity.Player;

namespace NexusForever.WorldServer.Tests
{
    public class ClientReplayLevelHandlerTests
    {
        [Theory]
        [InlineData(2u, 2u, (byte)1)]
        [InlineData(25u, 17u, (byte)16)]
        [InlineData(50u, 50u, (byte)49)]
        public void AchievedBuildLevel_CastsExactReplayTier(
            uint playerLevel,
            uint requestedLevel,
            byte expectedTier)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Level).Returns(playerLevel);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            new ClientReplayLevelHandler().HandleMessage(
                session.Object,
                CreateMessage(requestedLevel));

            player.Verify(value => value.CastSpell(
                53378u,
                expectedTier,
                It.Is<ISpellParameters>(parameters => !parameters.UserInitiatedSpellCast)), Times.Once);
            player.Verify(value => value.CastSpell(
                It.IsAny<uint>(),
                It.IsAny<byte>(),
                It.IsAny<ISpellParameters>()), Times.Once);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        [InlineData(51u)]
        [InlineData(uint.MaxValue)]
        public void OutOfBuildRange_IsRejectedBeforePlayerAuthorityReadOrCast(uint requestedLevel)
        {
            var player = new Mock<IPlayer>();
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientReplayLevelHandler().HandleMessage(
                    session.Object,
                    CreateMessage(requestedLevel)));

            player.VerifyGet(value => value.Level, Times.Never);
            player.Verify(value => value.CastSpell(
                It.IsAny<uint>(),
                It.IsAny<byte>(),
                It.IsAny<ISpellParameters>()), Times.Never);
        }

        [Fact]
        public void UnachievedBuildLevel_IsRejectedBeforeCast()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Level).Returns(10u);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientReplayLevelHandler().HandleMessage(
                    session.Object,
                    CreateMessage(11u)));

            player.VerifyGet(value => value.Level, Times.Once);
            player.Verify(value => value.CastSpell(
                It.IsAny<uint>(),
                It.IsAny<byte>(),
                It.IsAny<ISpellParameters>()), Times.Never);
        }

        private static ClientReplayLevelUp CreateMessage(uint level)
        {
            var message = new ClientReplayLevelUp();
            typeof(ClientReplayLevelUp)
                .GetProperty(
                    nameof(ClientReplayLevelUp.Level),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, level);
            return message;
        }
    }
}
