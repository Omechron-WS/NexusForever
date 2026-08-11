using System.Reflection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Spell;
using Moq;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientCancelEffectHandlerTests
    {
        [Fact]
        public void MatchingCastingId_FinishesExactSpellWithoutImmediateAcknowledgement()
        {
            var operations = new List<string>();
            var spell = new Mock<ISpell>();
            spell.SetupGet(value => value.CastingId).Returns(77u);
            spell.Setup(value => value.Finish()).Callback(() => operations.Add("finish"));
            var player = new Mock<IPlayer>();
            player.Setup(value => value.GetActiveSpell(55u)).Returns(spell.Object);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            new ClientCancelEffectHandler().HandleMessage(
                session.Object,
                CreateMessage(55u));

            Assert.Equal(["finish"], operations);
            spell.Verify(value => value.Finish(), Times.Once);
            player.Verify(value => value.EnqueueToVisible(
                It.IsAny<NexusForever.Network.Message.IWritable>(), It.IsAny<bool>()), Times.Never);
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
                It.IsAny<NexusForever.Network.Message.IWritable>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void CleanupFailure_IsContainedWithoutImmediateAcknowledgement()
        {
            var spell = new Mock<ISpell>();
            spell.SetupGet(value => value.CastingId).Returns(77u);
            spell.Setup(value => value.Finish())
                .Throws(new InvalidOperationException("Test cleanup failure."));
            var player = new Mock<IPlayer>();
            player.Setup(value => value.GetActiveSpell(55u)).Returns(spell.Object);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            Exception exception = Record.Exception(() =>
                new ClientCancelEffectHandler().HandleMessage(
                    session.Object,
                    CreateMessage(55u)));

            Assert.Null(exception);
            spell.Verify(value => value.Finish(), Times.Once);
            player.Verify(value => value.EnqueueToVisible(
                It.IsAny<NexusForever.Network.Message.IWritable>(), It.IsAny<bool>()), Times.Never);
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
