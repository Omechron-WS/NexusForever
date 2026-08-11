using System.Reflection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Matching.Match;
using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.Internal;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    public sealed class PlayerChairLifecycleTests
    {
        private const uint PlayerGuid = 10u;
        private const uint ChairGuid = 20u;

        [Fact]
        public void Unsit_ClearsStateAndPreservesPacketOrder()
        {
            ChairHarness harness = CreateHarness();
            harness.Player.Sit(harness.Chair.Object);
            harness.Packets.Clear();

            harness.Player.Unsit();

            Assert.False(harness.Player.IsSitting);
            AssertDetachPackets(harness.Packets);

            harness.Player.Unsit();
            Assert.Equal(2, harness.Packets.Count);
        }

        [Fact]
        public void Unsit_MissingChairClearsStateAndStillDetachesPlayer()
        {
            ChairHarness harness = CreateHarness();
            harness.Player.Sit(harness.Chair.Object);
            harness.Player.ForgetVisible(ChairGuid);
            harness.Packets.Clear();

            Exception exception = Record.Exception(harness.Player.Unsit);

            Assert.Null(exception);
            Assert.False(harness.Player.IsSitting);
            ServerUnitSetChair packet = Assert.IsType<ServerUnitSetChair>(Assert.Single(harness.Packets));
            Assert.Equal(PlayerGuid, packet.UnitId);
            Assert.Equal(0u, packet.UnitIdChair);
            Assert.False(packet.WaitForUnit);
        }

        [Fact]
        public void Unsit_NotificationFailuresAreContainedAndBothNotificationsAreAttemptedInOrder()
        {
            ChairHarness harness = CreateHarness();
            harness.Player.Sit(harness.Chair.Object);
            harness.Packets.Clear();
            var attempts = new List<string>();
            ConfigureDetachNotificationFailures(harness, attempts);

            Exception exception = Record.Exception(harness.Player.Unsit);

            Assert.Null(exception);
            Assert.False(harness.Player.IsSitting);
            Assert.Empty(harness.Packets);
            Assert.Equal(["chair-release", "player-detach"], attempts);
        }

        [Fact]
        public void RemoveVisible_CurrentChairDetachesBeforeDestroyingEntity()
        {
            ChairHarness harness = CreateHarness();
            harness.Player.Sit(harness.Chair.Object);
            harness.Packets.Clear();

            harness.Player.RemoveVisible(harness.Chair.Object);

            Assert.False(harness.Player.IsSitting);
            Assert.Null(harness.Player.GetVisible<IWorldEntity>(ChairGuid));
            Assert.Collection(
                harness.Packets,
                AssertChairReleased,
                AssertPlayerDetached,
                packet =>
                {
                    ServerEntityDestroy destroy = Assert.IsType<ServerEntityDestroy>(packet);
                    Assert.Equal(ChairGuid, destroy.Guid);
                    Assert.True(destroy.Unknown0);
                });
        }

        [Fact]
        public void RemoveVisible_NotificationFailuresDoNotAbortVisibilityCleanup()
        {
            ChairHarness harness = CreateHarness();
            harness.Player.Sit(harness.Chair.Object);
            harness.Packets.Clear();
            var attempts = new List<string>();
            ConfigureDetachNotificationFailures(harness, attempts);

            Exception exception = Record.Exception(() => harness.Player.RemoveVisible(harness.Chair.Object));

            Assert.Null(exception);
            Assert.False(harness.Player.IsSitting);
            Assert.Null(harness.Player.GetVisible<IWorldEntity>(ChairGuid));
            Assert.Equal(["chair-release", "player-detach"], attempts);
            ServerEntityDestroy destroy = Assert.IsType<ServerEntityDestroy>(Assert.Single(harness.Packets));
            Assert.Equal(ChairGuid, destroy.Guid);
        }

        [Fact]
        public void OnRemoveFromMap_DetachesChairBeforeBaseClearsVisibilityAndMap()
        {
            ChairHarness harness = CreateHarness();
            harness.Player.Sit(harness.Chair.Object);
            harness.Packets.Clear();
            harness.Chair
                .Setup(value => value.RemoveVisible(harness.Player))
                .Callback(() =>
                {
                    Assert.False(harness.Player.IsSitting);
                    AssertDetachPackets(harness.Packets);
                });

            harness.Player.OnRemoveFromMap();

            Assert.False(harness.Player.IsSitting);
            Assert.False(harness.Player.InWorld);
            Assert.Equal(0u, harness.Player.Guid);
            AssertDetachPackets(harness.Packets);
            harness.Chair.Verify(value => value.RemoveVisible(harness.Player), Times.Once);
        }

        [Fact]
        public void OnRemoveFromMap_NotificationFailuresDoNotAbortMapCleanup()
        {
            ChairHarness harness = CreateHarness();
            harness.Player.Sit(harness.Chair.Object);
            harness.Packets.Clear();
            var attempts = new List<string>();
            ConfigureDetachNotificationFailures(harness, attempts);

            Exception exception = Record.Exception(harness.Player.OnRemoveFromMap);

            Assert.Null(exception);
            Assert.False(harness.Player.IsSitting);
            Assert.False(harness.Player.InWorld);
            Assert.Equal(0u, harness.Player.Guid);
            Assert.Equal(["chair-release", "player-detach"], attempts);
            Assert.Empty(harness.Packets);
            harness.Chair.Verify(value => value.RemoveVisible(harness.Player), Times.Once);
        }

        private static ChairHarness CreateHarness()
        {
            var packets = new List<IWritable>();
            var session = new Mock<IGameSession>();
            session
                .Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(packets.Add);

            var map = new Mock<IBaseMap>();
            map.SetupGet(value => value.Entry).Returns(new WorldEntry { Id = 1u });

            var player = new TestPlayer(
                Mock.Of<IMovementManager>(),
                Mock.Of<IInternalMessagePublisher>(),
                Mock.Of<IEntityFactory>(),
                Mock.Of<IMatchingManager>(),
                Mock.Of<IMatchManager>(),
                Mock.Of<ICurrencyManager>(),
                Mock.Of<IGuildManager>(),
                Mock.Of<IResidenceManager>());
            player.PlaceInWorld(session.Object, map.Object, PlayerGuid);
            player.TrackVisible(player);

            var chair = new Mock<IWorldEntity>();
            chair.SetupGet(value => value.Guid).Returns(ChairGuid);
            chair
                .Setup(value => value.EnqueueToVisible(It.IsAny<IWritable>(), true))
                .Callback<IWritable, bool>((packet, _) => packets.Add(packet));
            player.TrackVisible(chair.Object);

            return new ChairHarness(player, chair, session, packets);
        }

        private static void ConfigureDetachNotificationFailures(ChairHarness harness, ICollection<string> attempts)
        {
            harness.Chair
                .Setup(value => value.EnqueueToVisible(It.IsAny<IWritable>(), true))
                .Callback<IWritable, bool>((_, _) =>
                {
                    attempts.Add("chair-release");
                    throw new InvalidOperationException("chair release failure");
                });
            harness.Session
                .Setup(value => value.EnqueueMessageEncrypted(
                    It.Is<IWritable>(packet => packet is ServerUnitSetChair)))
                .Callback<IWritable>(_ =>
                {
                    attempts.Add("player-detach");
                    throw new InvalidOperationException("player detach failure");
                });
        }

        private static void AssertDetachPackets(IReadOnlyList<IWritable> packets)
        {
            Assert.Collection(packets, AssertChairReleased, AssertPlayerDetached);
        }

        private static void AssertChairReleased(IWritable packet)
        {
            ServerUnitInUse chair = Assert.IsType<ServerUnitInUse>(packet);
            Assert.Equal(ChairGuid, chair.UnitId);
            Assert.False(chair.InUse);
        }

        private static void AssertPlayerDetached(IWritable packet)
        {
            ServerUnitSetChair player = Assert.IsType<ServerUnitSetChair>(packet);
            Assert.Equal(PlayerGuid, player.UnitId);
            Assert.Equal(0u, player.UnitIdChair);
            Assert.False(player.WaitForUnit);
        }

        private sealed record ChairHarness(
            TestPlayer Player,
            Mock<IWorldEntity> Chair,
            Mock<IGameSession> Session,
            List<IWritable> Packets);

        private sealed class TestPlayer : Player
        {
            private static readonly PropertyInfo MapProperty = typeof(GridEntity).GetProperty(
                nameof(Map),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly PropertyInfo SessionProperty = typeof(Player).GetProperty(
                nameof(Session),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            public TestPlayer(
                IMovementManager movementManager,
                IInternalMessagePublisher messagePublisher,
                IEntityFactory entityFactory,
                IMatchingManager matchingManager,
                IMatchManager matchManager,
                ICurrencyManager currencyManager,
                IGuildManager guildManager,
                IResidenceManager residenceManager)
                : base(
                    movementManager,
                    messagePublisher,
                    entityFactory,
                    matchingManager,
                    matchManager,
                    currencyManager,
                    guildManager,
                    residenceManager)
            {
            }

            public void PlaceInWorld(IGameSession session, IBaseMap map, uint guid)
            {
                Guid = guid;
                MapProperty.GetSetMethod(true).Invoke(this, [map]);
                SessionProperty.GetSetMethod(true).Invoke(this, [session]);
            }

            public void TrackVisible(IGridEntity entity)
            {
                visibleEntities.Add(entity.Guid, entity);
            }

            public void ForgetVisible(uint guid)
            {
                visibleEntities.Remove(guid);
            }
        }
    }
}
