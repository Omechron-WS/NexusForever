using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Static.Matching;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Matching;
using MatchingMatchType = NexusForever.Game.Static.Matching.MatchType;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientMatchingQueueRandomHandlerTests
    {
        public static TheoryData<int> ClientMatchTypes => new()
        {
            0,
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8,
            9,
            10,
            11,
            12,
            13,
            14,
            15
        };

        public static TheoryData<int> OutsideClientDomain => new()
        {
            -1,
            16,
            17,
            18,
            19,
            20,
            21,
            22,
            23,
            24,
            25,
            26,
            27,
            28,
            29,
            30,
            31
        };

        [Theory]
        [MemberData(nameof(OutsideClientDomain))]
        public void OutsideClientDomain_IsRejectedBeforeSessionPlayerOrManagerAccess(int value)
        {
            var matchingManager = new Mock<IMatchingManager>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientMatchingQueueRandomHandler(matchingManager.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateMessage((MatchingMatchType)value, Role.DPS)));

            session.VerifyNoOtherCalls();
            matchingManager.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ClientMatchTypes))]
        public void ClientMatchType_ForwardsExactPlayerRoleAndTypeOnce(int value)
        {
            MatchingMatchType matchType = (MatchingMatchType)value;
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(item => item.Player).Returns(player.Object);
            var matchingManager = new Mock<IMatchingManager>(MockBehavior.Strict);
            matchingManager.Setup(item => item.JoinRandomQueue(player.Object, Role.DPS, matchType));
            var handler = new ClientMatchingQueueRandomHandler(matchingManager.Object);

            handler.HandleMessage(session.Object, CreateMessage(matchType, Role.DPS));

            session.VerifyGet(item => item.Player, Times.Once);
            matchingManager.Verify(item => item.JoinRandomQueue(player.Object, Role.DPS, matchType), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            matchingManager.VerifyNoOtherCalls();
        }

        private static ClientMatchingQueueRandom CreateMessage(MatchingMatchType matchType, Role roles)
        {
            var message = new ClientMatchingQueueRandom();
            typeof(ClientMatchingQueueRandom)
                .GetProperty(nameof(ClientMatchingQueueRandom.MatchType), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, matchType);
            typeof(ClientMatchingQueueRandom)
                .GetProperty(nameof(ClientMatchingQueueRandom.Roles), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, roles);
            return message;
        }
    }
}
