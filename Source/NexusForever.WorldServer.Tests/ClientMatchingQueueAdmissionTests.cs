using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Static.Matching;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Matching;
using MatchingMatchType = NexusForever.Game.Static.Matching.MatchType;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientMatchingQueueAdmissionTests
    {
        private const ushort MatchingGameTypeId = 42;
        private const Role Roles = Role.Tank | Role.DPS;
        private const MatchingQueueFlags QueueFlags = MatchingQueueFlags.AsMercenary | MatchingQueueFlags.Veteran;

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

        public static TheoryData<int> ReservedMatchTypes => new()
        {
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
        [MemberData(nameof(ReservedMatchTypes))]
        public void ReservedMatchType_SoloIsRejectedBeforeSessionOrManagerAccess(int value)
        {
            var matchingManager = new Mock<IMatchingManager>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientMatchingQueueHandler(matchingManager.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreateSoloMessage((MatchingMatchType)value)));

            session.VerifyNoOtherCalls();
            matchingManager.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ReservedMatchTypes))]
        public void ReservedMatchType_PartyIsRejectedBeforeSessionOrManagerAccess(int value)
        {
            var matchingManager = new Mock<IMatchingManager>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            var handler = new ClientMatchingQueuePartyHandler(matchingManager.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                handler.HandleMessage(session.Object, CreatePartyMessage((MatchingMatchType)value)));

            session.VerifyNoOtherCalls();
            matchingManager.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ClientMatchTypes))]
        public void ClientMatchType_SoloForwardsExactPacketFieldsOnce(int value)
        {
            MatchingMatchType matchType = (MatchingMatchType)value;
            ClientMatchingQueue message = CreateSoloMessage(matchType);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(item => item.Player).Returns(player.Object);
            var matchingManager = new Mock<IMatchingManager>(MockBehavior.Strict);
            matchingManager.Setup(item => item.JoinQueue(
                player.Object,
                Roles,
                matchType,
                message.MapData.Maps,
                MatchingGameTypeId,
                QueueFlags));
            var handler = new ClientMatchingQueueHandler(matchingManager.Object);

            handler.HandleMessage(session.Object, message);

            session.VerifyGet(item => item.Player, Times.Once);
            matchingManager.Verify(item => item.JoinQueue(
                player.Object,
                Roles,
                matchType,
                message.MapData.Maps,
                MatchingGameTypeId,
                QueueFlags), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            matchingManager.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ClientMatchTypes))]
        public void ClientMatchType_PartyForwardsExactPacketFieldsOnce(int value)
        {
            MatchingMatchType matchType = (MatchingMatchType)value;
            ClientMatchingQueueParty message = CreatePartyMessage(matchType);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var session = new Mock<IWorldSession>(MockBehavior.Strict);
            session.SetupGet(item => item.Player).Returns(player.Object);
            var matchingManager = new Mock<IMatchingManager>(MockBehavior.Strict);
            matchingManager.Setup(item => item.JoinPartyQueue(
                player.Object,
                Roles,
                matchType,
                message.MapData.Maps,
                MatchingGameTypeId,
                QueueFlags));
            var handler = new ClientMatchingQueuePartyHandler(matchingManager.Object);

            handler.HandleMessage(session.Object, message);

            session.VerifyGet(item => item.Player, Times.Once);
            matchingManager.Verify(item => item.JoinPartyQueue(
                player.Object,
                Roles,
                matchType,
                message.MapData.Maps,
                MatchingGameTypeId,
                QueueFlags), Times.Once);
            session.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            matchingManager.VerifyNoOtherCalls();
        }

        private static ClientMatchingQueue CreateSoloMessage(MatchingMatchType matchType)
        {
            var message = new ClientMatchingQueue();
            SetProperty(message, nameof(ClientMatchingQueue.MapData), CreateMap(matchType));
            SetProperty(message, nameof(ClientMatchingQueue.Roles), Roles);
            return message;
        }

        private static ClientMatchingQueueParty CreatePartyMessage(MatchingMatchType matchType)
        {
            var message = new ClientMatchingQueueParty();
            SetProperty(message, nameof(ClientMatchingQueueParty.MapData), CreateMap(matchType));
            SetProperty(message, nameof(ClientMatchingQueueParty.Roles), Roles);
            return message;
        }

        private static MatchingMap CreateMap(MatchingMatchType matchType)
        {
            var map = new MatchingMap();
            map.Maps.AddRange([11u, 22u]);
            SetProperty(map, nameof(MatchingMap.MatchType), matchType);
            SetProperty(map, nameof(MatchingMap.MatchingGameTypeId), MatchingGameTypeId);
            SetProperty(map, nameof(MatchingMap.QueueFlags), QueueFlags);
            return map;
        }

        private static void SetProperty<T>(object target, string name, T value)
        {
            target.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(target, value);
        }
    }
}
