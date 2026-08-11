using Moq;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Housing;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Housing;
using GameIdentity = NexusForever.Game.Abstract.Identity;

namespace NexusForever.WorldServer.Tests.Housing
{
    public sealed class RandomCommunityListHandlerTests
    {
        [Fact]
        public void Request_ReconstructsExactPublicCommunitySnapshotsInSinglePacket()
        {
            IPublicCommunity first = CreateCommunity(1, 10ul, "Leader One", "Community One");
            IPublicCommunity second = CreateCommunity(2, 20ul, "Leader Two", "Community Two");
            var globalResidenceManager = new Mock<IGlobalResidenceManager>();
            globalResidenceManager
                .Setup(manager => manager.GetRandomVisitableCommunities())
                .Returns([first, second]);
            var session = new Mock<IWorldSession>();
            var handler = new ClientHousingRequestRandomCommunityListHandler(
                globalResidenceManager.Object,
                Mock.Of<IRealmContext>());

            handler.HandleMessage(session.Object, new ClientHousingRequestRandomCommunityList());

            session.Verify(
                value => value.EnqueueMessageEncrypted(
                    It.Is<ServerHousingRandomCommunityList>(message =>
                        message.Communities.Count == 2
                        && message.Communities[0].GuildIdentity.RealmId == 1
                        && message.Communities[0].GuildIdentity.Id == 10ul
                        && message.Communities[0].CommunityLeader == "Leader One"
                        && message.Communities[0].Name == "Community One"
                        && message.Communities[1].GuildIdentity.RealmId == 2
                        && message.Communities[1].GuildIdentity.Id == 20ul
                        && message.Communities[1].CommunityLeader == "Leader Two"
                        && message.Communities[1].Name == "Community Two")),
                Times.Once);
            session.Verify(
                value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Once);
        }

        [Fact]
        public void EmptyIndex_SendsSingleEmptyPacket()
        {
            var globalResidenceManager = new Mock<IGlobalResidenceManager>();
            globalResidenceManager
                .Setup(manager => manager.GetRandomVisitableCommunities())
                .Returns([]);
            var session = new Mock<IWorldSession>();
            var handler = new ClientHousingRequestRandomCommunityListHandler(
                globalResidenceManager.Object,
                Mock.Of<IRealmContext>());

            handler.HandleMessage(session.Object, new ClientHousingRequestRandomCommunityList());

            session.Verify(
                value => value.EnqueueMessageEncrypted(
                    It.Is<ServerHousingRandomCommunityList>(message =>
                        message.Communities.Count == 0)),
                Times.Once);
            session.Verify(
                value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Once);
        }

        private static IPublicCommunity CreateCommunity(
            ushort realmId,
            ulong id,
            string leader,
            string name)
        {
            var community = new Mock<IPublicCommunity>();
            community.SetupGet(value => value.GuildIdentity).Returns(new GameIdentity
            {
                RealmId = realmId,
                Id      = id
            });
            community.SetupGet(value => value.Owner).Returns(leader);
            community.SetupGet(value => value.Name).Returns(name);
            return community.Object;
        }
    }
}
