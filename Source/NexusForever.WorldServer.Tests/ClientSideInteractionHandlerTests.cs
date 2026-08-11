using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.CSI;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Entity;
using Moq;

namespace NexusForever.WorldServer.Tests
{
    public class ClientSideInteractionHandlerTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ActivationPackets_ForwardExactClientIdentifierWithoutEagerProgress(bool deferred)
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> entity) = CreateValidActivation();

            if (deferred)
            {
                var message = CreateMessage<ClientActivateUnitInteraction>(88u, 77u);
                new ClientActivateUnitInteractionHandler().HandleMessage(session.Object, message);
            }
            else
            {
                var message = CreateMessage<ClientActivateUnitCast>(88u, 77u);
                new ClientActivateUnitCastHandler().HandleMessage(session.Object, message);
            }

            entity.Verify(value => value.TryActivateCast(player.Object, 88u), Times.Once);
            Mock.Get(player.Object.QuestManager).Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<NexusForever.Game.Static.Quest.QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void ActivationPacket_OutOfRange_IsRejected()
        {
            (Mock<IWorldSession> session, _, Mock<IWorldEntity> entity) = CreateValidActivation();
            entity.SetupGet(value => value.Position).Returns(new Vector3(6f, 0f, 0f));
            var message = CreateMessage<ClientActivateUnitCast>(88u, 77u);

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientActivateUnitCastHandler().HandleMessage(session.Object, message));

            entity.Verify(value => value.TryActivateCast(It.IsAny<IPlayer>(), It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void ActivationPacket_TerminalInteractionAwaitingCleanupIsIgnored()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> entity) = CreateValidActivation();
            var interaction = new Mock<IClientSideInteractionSpell>();
            interaction.SetupGet(value => value.IsInteractionPending).Returns(false);
            player.Setup(value => value.GetActiveSpell(It.IsAny<Func<ISpell, bool>>()))
                .Returns((Func<ISpell, bool> predicate) => predicate(interaction.Object)
                    ? interaction.Object
                    : null);
            var message = CreateMessage<ClientActivateUnitCast>(88u, 77u);

            Exception exception = Record.Exception(() =>
                new ClientActivateUnitCastHandler().HandleMessage(session.Object, message));

            Assert.Null(exception);
            entity.Verify(value => value.TryActivateCast(It.IsAny<IPlayer>(), It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void GeneralInteractionFollowedByCsi_DoesNotGrantActivateProgressBeforeSuccess()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> entity) = CreateValidActivation();
            var assetManager = new Mock<NexusForever.Game.Abstract.IAssetManager>();
            var generalHandler = new ClientEntityInteractionHandler(
                Mock.Of<ILogger<ClientEntityInteractionHandler>>(),
                assetManager.Object);
            var generalMessage = new ClientEntityInteract();
            SetProperty(generalMessage, nameof(ClientEntityInteract.Guid), 77u);
            SetProperty(generalMessage, nameof(ClientEntityInteract.Event), (byte)37);

            generalHandler.HandleMessage(session.Object, generalMessage);
            new ClientActivateUnitCastHandler().HandleMessage(
                session.Object,
                CreateMessage<ClientActivateUnitCast>(88u, 77u));

            Mock.Get(player.Object.QuestManager).Verify(
                manager => manager.ObjectiveUpdate(QuestObjectiveType.ActivateEntity, It.IsAny<uint>(), It.IsAny<uint>()),
                Times.Never);
            entity.Verify(value => value.TryActivateCast(player.Object, 88u), Times.Once);
        }

        [Fact]
        public void GeneralInteraction_OutOfRangeDoesNotGrantQuestProgress()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> entity) = CreateValidActivation();
            entity.SetupGet(value => value.Position).Returns(new Vector3(6f, 0f, 0f));
            var handler = new ClientEntityInteractionHandler(
                Mock.Of<ILogger<ClientEntityInteractionHandler>>(),
                Mock.Of<NexusForever.Game.Abstract.IAssetManager>());
            var message = new ClientEntityInteract();
            SetProperty(message, nameof(ClientEntityInteract.Guid), 77u);
            SetProperty(message, nameof(ClientEntityInteract.Event), (byte)37);

            Assert.Throws<InvalidPacketValueException>(() => handler.HandleMessage(session.Object, message));

            Mock.Get(player.Object.QuestManager).Verify(
                manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void ActivationPacket_UnavailableSpellIsHandledWithoutDisconnecting()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> entity) = CreateValidActivation();
            entity.Setup(value => value.TryActivateCast(player.Object, 88u)).Returns(false);

            Exception exception = Record.Exception(() =>
                new ClientActivateUnitCastHandler().HandleMessage(
                    session.Object,
                    CreateMessage<ClientActivateUnitCast>(88u, 77u)));

            Assert.Null(exception);
            entity.Verify(value => value.TryActivateCast(player.Object, 88u), Times.Once);
        }

        [Fact]
        public void ActivationPacket_EntityWithoutCastActivationIsRejected()
        {
            (Mock<IWorldSession> session, _, Mock<IWorldEntity> entity) = CreateValidActivation();
            entity.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                ActivateSpellMaxRange = 5f
            });

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientActivateUnitCastHandler().HandleMessage(
                    session.Object,
                    CreateMessage<ClientActivateUnitCast>(88u, 77u)));

            entity.Verify(value => value.TryActivateCast(It.IsAny<IPlayer>(), It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void DirectActivation_CannotBypassCastActivation()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> entity) = CreateValidActivation();
            var message = new ClientActivateUnit();
            SetProperty(message, nameof(ClientActivateUnit.UnitId), 77u);

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientActivateUnitHandler().HandleMessage(session.Object, message));

            entity.Verify(value => value.OnActivate(player.Object), Times.Never);
        }

        [Fact]
        public void DirectActivation_ValidNonCastEntityIsForwarded()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> entity) = CreateValidActivation();
            entity.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                ActivateSpellMaxRange = 5f
            });
            var message = new ClientActivateUnit();
            SetProperty(message, nameof(ClientActivateUnit.UnitId), 77u);

            new ClientActivateUnitHandler().HandleMessage(session.Object, message);

            entity.Verify(value => value.OnActivate(player.Object), Times.Once);
        }

        [Fact]
        public void DirectActivation_OutOfRangeIsRejected()
        {
            (Mock<IWorldSession> session, Mock<IPlayer> player, Mock<IWorldEntity> entity) = CreateValidActivation();
            entity.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                ActivateSpellMaxRange = 5f
            });
            entity.SetupGet(value => value.Position).Returns(new Vector3(6f, 0f, 0f));
            var message = new ClientActivateUnit();
            SetProperty(message, nameof(ClientActivateUnit.UnitId), 77u);

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientActivateUnitHandler().HandleMessage(session.Object, message));

            entity.Verify(value => value.OnActivate(player.Object), Times.Never);
        }

        [Theory]
        [InlineData(ClientSideInteractionResult.Fail)]
        [InlineData(ClientSideInteractionResult.Success)]
        [InlineData(ClientSideInteractionResult.Cancel)]
        public void ResultPacket_DispatchesSingleMatchingTerminalAction(ClientSideInteractionResult result)
        {
            var interaction = new Mock<IClientSideInteractionSpell>();
            interaction.SetupGet(value => value.RequiresClientResult).Returns(true);
            interaction.Setup(value => value.FailClientInteraction()).Returns(true);
            interaction.Setup(value => value.SucceedClientInteraction()).Returns(true);
            interaction.Setup(value => value.CancelClientInteraction()).Returns(true);
            var player = new Mock<IPlayer>();
            player.Setup(value => value.GetActiveSpell(456u)).Returns(interaction.Object);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);
            ClientSpellInteractionResult message = CreateResultMessage(456u, result, 999u);

            new ClientSpellInteractionResultHandler().HandleMessage(session.Object, message);

            interaction.Verify(value => value.FailClientInteraction(),
                result == ClientSideInteractionResult.Fail ? Times.Once : Times.Never);
            interaction.Verify(value => value.SucceedClientInteraction(),
                result == ClientSideInteractionResult.Success ? Times.Once : Times.Never);
            interaction.Verify(value => value.CancelClientInteraction(),
                result == ClientSideInteractionResult.Cancel ? Times.Once : Times.Never);
        }

        [Fact]
        public void ResultPacket_MissingAndReplayedAreIgnoredButLiveMismatchesAreRejected()
        {
            var player = new Mock<IPlayer>();
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);
            var handler = new ClientSpellInteractionResultHandler();

            Exception missingException = Record.Exception(() => handler.HandleMessage(
                session.Object,
                CreateResultMessage(1u, ClientSideInteractionResult.Success, 0u)));
            Assert.Null(missingException);

            player.Setup(value => value.GetActiveSpell(2u)).Returns(Mock.Of<ISpell>());
            Assert.Throws<InvalidPacketValueException>(() => handler.HandleMessage(
                session.Object,
                CreateResultMessage(2u, ClientSideInteractionResult.Success, 0u)));

            var interaction = new Mock<IClientSideInteractionSpell>();
            interaction.SetupGet(value => value.RequiresClientResult).Returns(true);
            interaction.Setup(value => value.SucceedClientInteraction()).Returns(false);
            player.Setup(value => value.GetActiveSpell(3u)).Returns(interaction.Object);
            Exception replayException = Record.Exception(() => handler.HandleMessage(
                session.Object,
                CreateResultMessage(3u, ClientSideInteractionResult.Success, 0u)));
            Assert.Null(replayException);

            interaction.Setup(value => value.FailClientInteraction()).Returns(true);
            player.Setup(value => value.GetActiveSpell(4u)).Returns(interaction.Object);
            Assert.Throws<InvalidPacketValueException>(() => handler.HandleMessage(
                session.Object,
                CreateResultMessage(4u, (ClientSideInteractionResult)5, 0u)));

        }

        private static (Mock<IWorldSession> Session, Mock<IPlayer> Player, Mock<IWorldEntity> Entity) CreateValidActivation()
        {
            var map = new Mock<IBaseMap>();
            var entity = new Mock<IWorldEntity>();
            entity.SetupGet(value => value.InWorld).Returns(true);
            entity.SetupGet(value => value.Guid).Returns(77u);
            entity.SetupGet(value => value.Map).Returns(map.Object);
            entity.SetupGet(value => value.Position).Returns(new Vector3(3f, 0f, 0f));
            entity.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                Spell4IdActivate00    = 123u,
                ActivateSpellMaxRange = 5f
            });

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.InWorld).Returns(true);
            player.SetupGet(value => value.Map).Returns(map.Object);
            player.SetupGet(value => value.Position).Returns(Vector3.Zero);
            player.SetupGet(value => value.QuestManager).Returns(Mock.Of<IQuestManager>());
            player.Setup(value => value.GetVisible<IWorldEntity>(77u)).Returns(entity.Object);
            player.Setup(value => value.GetActiveSpell(It.IsAny<Func<ISpell, bool>>()))
                .Returns((ISpell)null);
            entity.Setup(value => value.TryActivateCast(player.Object, 88u)).Returns(true);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);
            return (session, player, entity);
        }

        private static T CreateMessage<T>(uint clientUniqueId, uint activateUnitId)
            where T : class, new()
        {
            var message = new T();
            SetProperty(message, "ClientUniqueId", clientUniqueId);
            SetProperty(message, "ActivateUnitId", activateUnitId);
            return message;
        }

        private static ClientSpellInteractionResult CreateResultMessage(
            uint castingId,
            ClientSideInteractionResult result,
            uint validation)
        {
            var message = new ClientSpellInteractionResult();
            SetProperty(message, nameof(ClientSpellInteractionResult.CastingId), castingId);
            SetProperty(message, nameof(ClientSpellInteractionResult.Result), result);
            SetProperty(message, nameof(ClientSpellInteractionResult.Validation), validation);
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
