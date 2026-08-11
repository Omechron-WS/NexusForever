using System.Reflection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity.Movement;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model.Entity;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Entity;
using Moq;

namespace NexusForever.WorldServer.Tests
{
    public class ClientDashHandlerTests
    {
        [Theory]
        [InlineData(DashDirection.Left, 25293u)]
        [InlineData(DashDirection.Right, 25294u)]
        [InlineData(DashDirection.Forward, 25295u)]
        [InlineData(DashDirection.Back, 25296u)]
        public void ValidDirection_CastsExactNonUserInitiatedSpell(DashDirection direction, uint expectedSpell4Id)
        {
            var player = new Mock<IPlayer>();
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            new ClientDashHandler().HandleMessage(session.Object, CreateMessage(direction));

            player.Verify(value => value.CastSpell(
                expectedSpell4Id,
                It.Is<ISpellParameters>(parameters => !parameters.UserInitiatedSpellCast)), Times.Once);
            player.Verify(value => value.CastSpell(
                It.IsAny<uint>(),
                It.IsAny<ISpellParameters>()), Times.Once);
        }

        [Theory]
        [InlineData((DashDirection)0)]
        [InlineData((DashDirection)5)]
        [InlineData((DashDirection)6)]
        [InlineData((DashDirection)7)]
        public void ReservedDirection_IsRejectedBeforeCast(DashDirection direction)
        {
            var player = new Mock<IPlayer>();
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientDashHandler().HandleMessage(session.Object, CreateMessage(direction)));

            player.Verify(value => value.CastSpell(
                It.IsAny<uint>(),
                It.IsAny<ISpellParameters>()), Times.Never);
        }

        private static ClientDash CreateMessage(DashDirection direction)
        {
            var message = new ClientDash();
            typeof(ClientDash)
                .GetProperty(
                    nameof(ClientDash.Direction),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, direction);
            return message;
        }
    }
}
