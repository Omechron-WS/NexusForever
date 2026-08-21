using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Static.Guild;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model.Guild;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Guild;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientGuildOperationHandlerTests
    {
        public static IEnumerable<object[]> RankOperations
        {
            get
            {
                yield return [GuildOperation.RankAdd];
                yield return [GuildOperation.RankDelete];
                yield return [GuildOperation.RankPermissions];
                yield return [GuildOperation.RankRename];
            }
        }

        public static IEnumerable<object[]> InvalidRankOperations
        {
            get
            {
                uint[] ranks = [10u, 255u, 256u, 258u, uint.MaxValue];
                foreach (object[] operation in RankOperations)
                foreach (uint rank in ranks)
                    yield return [operation[0], rank];
            }
        }

        public static IEnumerable<object[]> ValidRankOperations
        {
            get
            {
                foreach (object[] operation in RankOperations)
                {
                    yield return [operation[0], 0u];
                    yield return [operation[0], 9u];
                }
            }
        }

        [Theory]
        [MemberData(nameof(InvalidRankOperations))]
        public void RankOutsidePublishedSlots_IsRejectedBeforePlayerOrGuildAccess(
            GuildOperation operation,
            uint rank)
        {
            var manager = new Mock<IGlobalGuildManager>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientGuildOperationHandler(manager.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage(operation, rank)));

            session.VerifyNoOtherCalls();
            manager.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ValidRankOperations))]
        public void PublishedRankSlot_IsForwardedUnchanged(GuildOperation operation, uint rank)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);
            var manager = new Mock<IGlobalGuildManager>(MockBehavior.Strict);
            ClientGuildOperation forwarded = null;
            manager
                .Setup(value => value.HandleGuildOperation(player.Object, It.IsAny<ClientGuildOperation>()))
                .Callback<IPlayer, ClientGuildOperation>((_, message) => forwarded = message);
            var handler = new ClientGuildOperationHandler(manager.Object);
            ClientGuildOperation message = CreateMessage(operation, rank);

            handler.HandleMessage(session.Object, message);

            Assert.Same(message, forwarded);
            Assert.Equal(operation, forwarded.Operation);
            Assert.Equal(rank, forwarded.Rank);
            session.VerifyGet(value => value.Player, Times.Once);
            manager.Verify(value => value.HandleGuildOperation(player.Object, message), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            manager.VerifyNoOtherCalls();
        }

        [Fact]
        public void NonRankOperation_PreservesOpaqueRankValue()
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(value => value.Player).Returns(player.Object);
            var manager = new Mock<IGlobalGuildManager>(MockBehavior.Strict);
            ClientGuildOperation message = CreateMessage(GuildOperation.MemberPromote, uint.MaxValue);
            manager.Setup(value => value.HandleGuildOperation(player.Object, message));
            var handler = new ClientGuildOperationHandler(manager.Object);

            handler.HandleMessage(session.Object, message);

            session.VerifyGet(value => value.Player, Times.Once);
            manager.Verify(value => value.HandleGuildOperation(player.Object, message), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            manager.VerifyNoOtherCalls();
        }

        private static ClientGuildOperation CreateMessage(GuildOperation operation, uint rank)
        {
            var message = new ClientGuildOperation();
            typeof(ClientGuildOperation)
                .GetProperty(nameof(ClientGuildOperation.Operation), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, operation);
            typeof(ClientGuildOperation)
                .GetProperty(nameof(ClientGuildOperation.Rank), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, rank);
            return message;
        }
    }
}
