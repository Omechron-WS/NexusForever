using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    public class LogoutManagerTests
    {
        [Fact]
        public void Finish_ClearsThreatsBeforeLogoutNotificationAndCompletion()
        {
            var player = new Mock<IPlayer>();
            var threatManager = new Mock<IThreatManager>();
            var session = new Mock<IGameSession>();
            bool threatsCleared = false;
            bool completionInvoked = false;

            player.SetupGet(p => p.CharacterId).Returns(123uL);
            player.SetupGet(p => p.ThreatManager).Returns(threatManager.Object);
            player.SetupGet(p => p.Session).Returns(session.Object);
            threatManager
                .Setup(t => t.ClearThreatList())
                .Callback(() => threatsCleared = true);
            session
                .Setup(s => s.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                .Callback<IWritable>(_ => Assert.True(threatsCleared));

            var logoutManager = new LogoutManager(player.Object);
            logoutManager.OnTimerFinished += () =>
            {
                Assert.True(threatsCleared);
                completionInvoked = true;
            };

            logoutManager.Finish(LogoutReason.AccountDisconnected);

            Assert.Equal(LogoutState.Logout, logoutManager.State);
            Assert.True(completionInvoked);
            threatManager.Verify(t => t.ClearThreatList(), Times.Once);
            session.Verify(s => s.EnqueueMessageEncrypted(
                It.Is<ServerLogout>(message =>
                    !message.Requested &&
                    message.Reason == LogoutReason.AccountDisconnected)), Times.Once);
        }
    }
}
