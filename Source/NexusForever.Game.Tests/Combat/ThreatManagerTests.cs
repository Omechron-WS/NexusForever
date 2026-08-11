using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Combat;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using Moq;

namespace NexusForever.Game.Tests.Combat
{
    public class ThreatManagerTests
    {
        [Fact]
        public void Update_PlayerPairExpiresAtTenSecondsAndRemovesReciprocal()
        {
            PlayerThreatPair pair = CreatePlayerThreatPair();

            pair.FirstManager.Update(9d);

            Assert.True(pair.FirstManager.IsThreatened);
            Assert.True(pair.SecondManager.IsThreatened);

            pair.FirstManager.Update(1d);

            Assert.False(pair.FirstManager.IsThreatened);
            Assert.False(pair.SecondManager.IsThreatened);
            VerifyRemovalPacket(pair.First, 1u, 2u);
            VerifyRemovalPacket(pair.Second, 2u, 1u);
        }

        [Fact]
        public void Update_ThreatRefreshResetsBothPlayerTimersWithoutRecursion()
        {
            PlayerThreatPair pair = CreatePlayerThreatPair();
            pair.FirstManager.Update(9d);
            pair.SecondManager.Update(9d);

            pair.FirstManager.UpdateThreat(pair.Second.Object, 5);
            pair.FirstManager.Update(9d);
            pair.SecondManager.Update(9d);

            Assert.True(pair.FirstManager.IsThreatened);
            Assert.True(pair.SecondManager.IsThreatened);
            Assert.Equal(15u, pair.FirstManager.GetHostile(2u).Threat);
            Assert.Equal(1u, pair.SecondManager.GetHostile(1u).Threat);
            pair.First.Verify(p => p.OnThreatChange(It.IsAny<IHostileEntity>()), Times.Once);
            pair.Second.Verify(p => p.OnThreatChange(It.IsAny<IHostileEntity>()), Times.Never);

            pair.SecondManager.Update(1d);

            Assert.False(pair.FirstManager.IsThreatened);
            Assert.False(pair.SecondManager.IsThreatened);
        }

        [Fact]
        public void Update_PlayerCreaturePairDoesNotExpire()
        {
            var player = new Mock<IPlayer>();
            var creature = new Mock<IUnitEntity>();
            player.SetupGet(p => p.Guid).Returns(1u);
            creature.SetupGet(c => c.Guid).Returns(2u);

            var playerManager = new ThreatManager(player.Object);
            var creatureManager = new ThreatManager(creature.Object);
            player.SetupGet(p => p.ThreatManager).Returns(playerManager);
            creature.SetupGet(c => c.ThreatManager).Returns(creatureManager);
            player.Setup(p => p.GetVisible<IUnitEntity>(2u)).Returns(creature.Object);
            creature.Setup(c => c.GetVisible<IUnitEntity>(1u)).Returns(player.Object);

            playerManager.UpdateThreat(creature.Object, 10);
            playerManager.Update(double.MaxValue);
            creatureManager.Update(double.MaxValue);

            Assert.True(playerManager.IsThreatened);
            Assert.True(creatureManager.IsThreatened);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1d)]
        [InlineData(0d)]
        public void Update_InvalidOrNonPositiveElapsedTimeDoesNotAdvanceTimeout(double lastTick)
        {
            PlayerThreatPair pair = CreatePlayerThreatPair();

            pair.FirstManager.Update(lastTick);
            pair.FirstManager.Update(9d);

            Assert.True(pair.FirstManager.IsThreatened);
            Assert.True(pair.SecondManager.IsThreatened);
        }

        [Fact]
        public void RemoveHostile_NotificationFailureStillRemovesReciprocalRelationship()
        {
            PlayerThreatPair pair = CreatePlayerThreatPair();
            pair.First
                .Setup(p => p.EnqueueToVisible(It.IsAny<IWritable>(), It.IsAny<bool>()))
                .Throws(new InvalidOperationException("Test notification failure."));

            pair.FirstManager.RemoveHostile(2u);

            Assert.False(pair.FirstManager.IsThreatened);
            Assert.False(pair.SecondManager.IsThreatened);
            pair.First.Verify(p => p.OnThreatRemoveTarget(It.IsAny<IHostileEntity>()), Times.Once);
            pair.Second.Verify(p => p.OnThreatRemoveTarget(It.IsAny<IHostileEntity>()), Times.Once);
        }

        private static PlayerThreatPair CreatePlayerThreatPair()
        {
            var first = new Mock<IPlayer>();
            var second = new Mock<IPlayer>();
            first.SetupGet(p => p.Guid).Returns(1u);
            second.SetupGet(p => p.Guid).Returns(2u);

            var firstManager = new ThreatManager(first.Object);
            var secondManager = new ThreatManager(second.Object);
            first.SetupGet(p => p.ThreatManager).Returns(firstManager);
            second.SetupGet(p => p.ThreatManager).Returns(secondManager);
            first.Setup(p => p.GetVisible<IUnitEntity>(2u)).Returns(second.Object);
            second.Setup(p => p.GetVisible<IUnitEntity>(1u)).Returns(first.Object);

            firstManager.UpdateThreat(second.Object, 10);
            return new PlayerThreatPair(first, firstManager, second, secondManager);
        }

        private static void VerifyRemovalPacket(Mock<IPlayer> player, uint unitId, uint targetId)
        {
            player.Verify(p => p.EnqueueToVisible(
                It.Is<ServerEntityThreatUpdate>(message =>
                    message.UnitId == unitId &&
                    message.TargetId == targetId &&
                    message.ThreatLevel == 0u),
                false), Times.Once);
        }

        private sealed class PlayerThreatPair
        {
            public Mock<IPlayer> First { get; }
            public ThreatManager FirstManager { get; }
            public Mock<IPlayer> Second { get; }
            public ThreatManager SecondManager { get; }

            public PlayerThreatPair(
                Mock<IPlayer> first,
                ThreatManager firstManager,
                Mock<IPlayer> second,
                ThreatManager secondManager)
            {
                First = first;
                FirstManager = firstManager;
                Second = second;
                SecondManager = secondManager;
            }
        }
    }
}
