using System.Reflection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Spell;
using Moq;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientCancelEffectHandlerTests
    {
        [Fact]
        public void MatchingCastingId_FinishesExactSpellBeforeAcknowledgement()
        {
            var operations = new List<string>();
            var spell = new Mock<ISpell>();
            spell.SetupGet(value => value.CastingId).Returns(77u);
            spell.Setup(value => value.Finish()).Callback(() => operations.Add("finish"));
            var player = new Mock<IPlayer>();
            player.Setup(value => value.GetActiveSpell(55u)).Returns(spell.Object);
            player.Setup(value => value.EnqueueToVisible(It.IsAny<IWritable>(), true))
                .Callback<IWritable, bool>((_, _) => operations.Add("acknowledge"));
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            new ClientCancelEffectHandler().HandleMessage(
                session.Object,
                CreateMessage(55u));

            Assert.Equal(["finish", "acknowledge"], operations);
            spell.Verify(value => value.Finish(), Times.Once);
            player.Verify(value => value.EnqueueToVisible(
                It.Is<ServerSpellFinish>(packet => packet.ServerUniqueId == 77u),
                true), Times.Once);
        }

        [Fact]
        public void MissingCastingId_IsIgnoredWithoutAcknowledgement()
        {
            var player = new Mock<IPlayer>();
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            new ClientCancelEffectHandler().HandleMessage(
                session.Object,
                CreateMessage(55u));

            player.Verify(value => value.GetActiveSpell(55u), Times.Once);
            player.Verify(value => value.EnqueueToVisible(
                It.IsAny<IWritable>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void CleanupAndAcknowledgementFailures_AreContainedIndependently()
        {
            var spell = new Mock<ISpell>();
            spell.SetupGet(value => value.CastingId).Returns(77u);
            spell.Setup(value => value.Finish())
                .Throws(new InvalidOperationException("Test cleanup failure."));
            var player = new Mock<IPlayer>();
            player.Setup(value => value.GetActiveSpell(55u)).Returns(spell.Object);
            player.Setup(value => value.EnqueueToVisible(It.IsAny<IWritable>(), true))
                .Throws(new InvalidOperationException("Test packet failure."));
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            Exception exception = Record.Exception(() =>
                new ClientCancelEffectHandler().HandleMessage(
                    session.Object,
                    CreateMessage(55u)));

            Assert.Null(exception);
            spell.Verify(value => value.Finish(), Times.Once);
            player.Verify(value => value.EnqueueToVisible(
                It.IsAny<ServerSpellFinish>(), true), Times.Once);
        }

        private static ClientCancelEffect CreateMessage(uint castingId)
        {
            var message = new ClientCancelEffect();
            typeof(ClientCancelEffect)
                .GetProperty(
                    nameof(ClientCancelEffect.ServerUniqueId),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, castingId);
            return message;
        }
    }
}
