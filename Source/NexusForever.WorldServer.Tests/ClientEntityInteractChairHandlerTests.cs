using System.Numerics;
using System.Reflection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Entity;
using Moq;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientEntityInteractChairHandlerTests
    {
        private const uint ChairGuid = 77u;
        private const uint ChairActivationFlag = 0x200000u;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HandleMessage_UnresolvedRemoveBitDoesNotChangeValidatedAdmission(bool remove)
        {
            // The build-16042 meaning of Remove is unresolved, so preserve the existing ignored-bit behaviour.
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> chair) = CreateValidInteraction();

            new ClientEntityInteractChairHandler().HandleMessage(session.Object, CreateMessage(remove));

            player.Verify(value => value.Sit(chair.Object), Times.Once);
        }

        [Fact]
        public void HandleMessage_ChairOutsideMaximumRange_IsRejectedBeforeSitting()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> chair) = CreateValidInteraction();
            chair.SetupGet(value => value.Position).Returns(new Vector3(1.01f, 0f, 0f));

            AssertRejected(session, player);
        }

        [Fact]
        public void HandleMessage_ChairOnDifferentMap_IsRejectedBeforeSitting()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> chair) = CreateValidInteraction();
            chair.SetupGet(value => value.Map).Returns(Mock.Of<IBaseMap>());

            AssertRejected(session, player);
        }

        [Fact]
        public void HandleMessage_ChairRemovedFromVisibilityDuringValidation_IsRejectedBeforeSitting()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> chair) = CreateValidInteraction();
            player.SetupSequence(value => value.GetVisible<IWorldEntity>(ChairGuid))
                .Returns(chair.Object)
                .Returns((IWorldEntity)null);

            AssertRejected(session, player);
        }

        [Fact]
        public void HandleMessage_NonFiniteChairPosition_IsRejectedBeforeSitting()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> chair) = CreateValidInteraction();
            chair.SetupGet(value => value.Position).Returns(new Vector3(float.NaN, 0f, 0f));

            AssertRejected(session, player);
        }

        [Fact]
        public void HandleMessage_MissingCreatureEntry_IsRejectedBeforeSitting()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> chair) = CreateValidInteraction();
            chair.SetupGet(value => value.CreatureEntry).Returns((Creature2Entry)null);

            AssertRejected(session, player);
        }

        [Fact]
        public void HandleMessage_EntityWithoutChairFlag_IsRejectedBeforeSitting()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> chair) = CreateValidInteraction();
            chair.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                ActivateSpellMaxRange = 1f
            });

            AssertRejected(session, player);
        }

        private static void AssertRejected(Mock<IWorldSession> session, Mock<IPlayer> player)
        {
            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientEntityInteractChairHandler().HandleMessage(session.Object, CreateMessage(false)));
            player.Verify(value => value.Sit(It.IsAny<IWorldEntity>()), Times.Never);
        }

        private static (Mock<IWorldSession> Session, Mock<IPlayer> Player, Mock<IWorldEntity> Chair) CreateValidInteraction()
        {
            var map = new Mock<IBaseMap>();
            var chair = new Mock<IWorldEntity>();
            chair.SetupGet(value => value.InWorld).Returns(true);
            chair.SetupGet(value => value.Guid).Returns(ChairGuid);
            chair.SetupGet(value => value.Map).Returns(map.Object);
            chair.SetupGet(value => value.Position).Returns(new Vector3(1f, 0f, 0f));
            chair.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                ActivationFlags       = ChairActivationFlag,
                ActivateSpellMaxRange = 1f
            });

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.InWorld).Returns(true);
            player.SetupGet(value => value.Map).Returns(map.Object);
            player.SetupGet(value => value.Position).Returns(Vector3.Zero);
            player.Setup(value => value.GetVisible<IWorldEntity>(ChairGuid)).Returns(chair.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);
            return (session, player, chair);
        }

        private static ClientEntityInteractChair CreateMessage(bool remove)
        {
            var message = new ClientEntityInteractChair();
            SetProperty(message, nameof(ClientEntityInteractChair.ChairUnitId), ChairGuid);
            SetProperty(message, nameof(ClientEntityInteractChair.Remove), remove);
            return message;
        }

        private static void SetProperty<T>(object instance, string propertyName, T value)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(instance, value);
        }
    }
}
