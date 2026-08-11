using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Character;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Housing;
using NexusForever.Game.Static.Guild;
using NexusForever.Game.Static.Housing;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model.Housing;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Housing
{
    public class GlobalResidenceManagerPersistenceTests
    {
        [Fact]
        public async Task ShutdownAsync_MixedBatchAcknowledgesOnlySuccessfulResidence()
        {
            var successfulCommit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failedCommit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var database = new QueuedCharacterDatabase(
                new ControlledCharacterContext(successfulCommit.Task),
                new ControlledCharacterContext(failedCommit.Task));
            var databaseManager = new Mock<IDatabaseManager>();
            databaseManager.Setup(manager => manager.GetDatabase<CharacterDatabase>()).Returns(database);

            bool successfulAcknowledgement = false;
            bool failedAcknowledgement = false;
            Identity successfulIdentity = new() { RealmId = 1, Id = 1ul };
            Identity failedIdentity = new() { RealmId = 1, Id = 2ul };
            Mock<IResidence> successfulResidence = CreateResidence(successfulIdentity, 11ul, () => successfulAcknowledgement = true);
            Mock<IResidence> failedResidence = CreateResidence(failedIdentity, 12ul, () => failedAcknowledgement = true);
            var residenceFactory = new Mock<IFactory<IResidence>>();
            residenceFactory.SetupSequence(factory => factory.Resolve())
                .Returns(successfulResidence.Object)
                .Returns(failedResidence.Object);

            var manager = new GlobalResidenceManager(
                Mock.Of<ILogger<GlobalResidenceManager>>(),
                Mock.Of<IRealmContext>(),
                databaseManager.Object,
                Mock.Of<ICharacterManager>(),
                residenceFactory.Object);
            manager.CreateResidence(CreatePlayer("Successful", 11ul));
            manager.CreateResidence(CreatePlayer("Failed", 12ul));

            Task shutdown = manager.ShutdownAsync();

            Assert.Same(successfulResidence.Object, manager.GetResidence(successfulIdentity));
            Assert.Same(failedResidence.Object, manager.GetResidence(failedIdentity));
            Assert.False(successfulAcknowledgement);
            Assert.False(failedAcknowledgement);

            var expected = new InvalidOperationException("save failed");
            successfulCommit.SetResult(1);
            failedCommit.SetException(expected);
            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => shutdown);

            Assert.Contains(expected, exception.InnerExceptions);
            Assert.True(successfulAcknowledgement);
            Assert.False(failedAcknowledgement);
            Assert.Same(successfulResidence.Object, manager.GetResidence(successfulIdentity));
            Assert.Same(failedResidence.Object, manager.GetResidence(failedIdentity));
        }

        [Fact]
        public void RegisterResidenceVists_ReplayUpsertsCurrentSnapshot()
        {
            Identity identity = new() { RealmId = 1, Id = 7ul };
            string residenceName = "First name";
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(identity);
            residence.SetupGet(value => value.Name).Returns(() => residenceName);
            var manager = CreateManager();

            manager.RegisterResidenceVists(residence.Object, "First owner");
            residenceName = "Current name";
            manager.RegisterResidenceVists(residence.Object, "Current owner");

            IPublicResidence publicResidence = Assert.Single(manager.GetRandomVisitableResidences());
            Assert.Equal(identity, publicResidence.Identity);
            Assert.Equal("Current owner", publicResidence.Owner);
            Assert.Equal("Current name", publicResidence.Name);
        }

        [Fact]
        public void DeregisterResidenceVists_ReplayIsIdempotent()
        {
            Identity identity = new() { RealmId = 1, Id = 7ul };
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(identity);
            var manager = CreateManager();
            manager.RegisterResidenceVists(residence.Object, "Owner");

            manager.DeregisterResidenceVists(identity);
            manager.DeregisterResidenceVists(identity);

            Assert.Empty(manager.GetRandomVisitableResidences());
        }

        [Fact]
        public void RegisterCommunityVisits_ReplayUpsertsCurrentSnapshot()
        {
            Identity residenceIdentity = new() { RealmId = 1, Id = 31ul };
            Identity communityIdentity = new() { RealmId = 1, Id = 41ul };
            string communityName = "First community";
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            var community = new Mock<ICommunity>();
            community.SetupGet(value => value.Identity).Returns(communityIdentity);
            community.SetupGet(value => value.Name).Returns(() => communityName);
            var manager = CreateManager();

            manager.RegisterCommunityVisits(residence.Object, community.Object, "First leader");
            communityName = "Current community";
            manager.RegisterCommunityVisits(residence.Object, community.Object, "Current leader");

            IPublicCommunity publicCommunity = Assert.Single(manager.GetRandomVisitableCommunities());
            Assert.Equal(communityIdentity, publicCommunity.GuildIdentity);
            Assert.Equal("Current leader", publicCommunity.Owner);
            Assert.Equal("Current community", publicCommunity.Name);
        }

        [Fact]
        public void DeregisterCommunityVists_ReplayIsIdempotent()
        {
            Identity residenceIdentity = new() { RealmId = 1, Id = 31ul };
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            var community = new Mock<ICommunity>();
            community.SetupGet(value => value.Identity).Returns(
                new Identity { RealmId = 1, Id = 41ul });
            var manager = CreateManager();
            manager.RegisterCommunityVisits(residence.Object, community.Object, "Leader");

            manager.DeregisterCommunityVists(residenceIdentity);
            manager.DeregisterCommunityVists(residenceIdentity);

            Assert.Empty(manager.GetRandomVisitableCommunities());
        }

        [Theory]
        [InlineData(GuildFlag.None, true)]
        [InlineData(GuildFlag.CommunityPrivate, false)]
        public void StoreCommunityPath_DerivesPublicIndexFromPersistedFlag(
            GuildFlag flags,
            bool expectedVisitable)
        {
            Identity residenceIdentity = new() { RealmId = 1, Id = 31ul };
            Identity communityIdentity = new() { RealmId = 1, Id = 41ul };
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            residence.SetupGet(value => value.GuildOwnerIdentity).Returns(communityIdentity);
            var leader = new Mock<ICharacter>();
            leader.SetupGet(value => value.Name).Returns("Persisted leader");
            var characterManager = new Mock<ICharacterManager>();
            characterManager.Setup(manager => manager.GetCharacter(51ul)).Returns(leader.Object);
            var community = new Mock<ICommunity>();
            community.SetupGet(value => value.Identity).Returns(communityIdentity);
            community.SetupGet(value => value.Name).Returns("Persisted community");
            community.SetupGet(value => value.Residence).Returns(residence.Object);
            community.SetupGet(value => value.LeaderId).Returns(51ul);
            community.SetupGet(value => value.Flags).Returns(flags);
            var manager = CreateManager(characterManager.Object);

            StorePersistedCommunity(manager, residence.Object, community.Object);

            Assert.Same(residence.Object, manager.GetCommunityByOwner(communityIdentity));
            Assert.Equal(expectedVisitable, manager.GetRandomVisitableCommunities().Any());
            if (expectedVisitable)
            {
                IPublicCommunity publicCommunity = Assert.Single(manager.GetRandomVisitableCommunities());
                Assert.Equal(communityIdentity, publicCommunity.GuildIdentity);
                Assert.Equal("Persisted leader", publicCommunity.Owner);
                Assert.Equal("Persisted community", publicCommunity.Name);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StoreCommunityPath_MalformedLeaderRemainsUnlistedWithoutChangingPersistedFlag(
            bool hasLeaderId)
        {
            Identity residenceIdentity = new() { RealmId = 1, Id = 31ul };
            Identity communityIdentity = new() { RealmId = 1, Id = 41ul };
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            residence.SetupGet(value => value.GuildOwnerIdentity).Returns(communityIdentity);
            var community = new Mock<ICommunity>();
            community.SetupGet(value => value.Identity).Returns(communityIdentity);
            community.SetupGet(value => value.Name).Returns("Community");
            community.SetupGet(value => value.Residence).Returns(residence.Object);
            community.SetupGet(value => value.LeaderId).Returns(hasLeaderId ? 51ul : null);
            community.SetupGet(value => value.Flags).Returns(GuildFlag.None);
            var manager = CreateManager();

            StorePersistedCommunity(manager, residence.Object, community.Object);

            Assert.Empty(manager.GetRandomVisitableCommunities());
            Assert.Equal(GuildFlag.None, community.Object.Flags);
            community.Verify(
                value => value.SetCommunityPrivate(It.IsAny<bool>()),
                Times.Never);
        }

        [Theory]
        [InlineData(ResidencePrivacyLevel.Public, true)]
        [InlineData(ResidencePrivacyLevel.Private, false)]
        [InlineData(ResidencePrivacyLevel.NeighboursOnly, false)]
        [InlineData(ResidencePrivacyLevel.RoommatesOnly, false)]
        public void StoreResidencePath_DerivesPublicIndexFromAuthoritativePrivacy(
            ResidencePrivacyLevel privacyLevel,
            bool expectedVisitable)
        {
            Identity ownerIdentity = new() { RealmId = 1, Id = 11ul };
            Identity residenceIdentity = new() { RealmId = 1, Id = 21ul };
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Identity).Returns(ownerIdentity);
            player.SetupGet(value => value.Name).Returns("Owner");
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            residence.SetupGet(value => value.OwnerIdentity).Returns(ownerIdentity);
            residence.SetupGet(value => value.Name).Returns("Residence");
            residence.SetupGet(value => value.PrivacyLevel).Returns(privacyLevel);
            var residenceFactory = new Mock<IFactory<IResidence>>();
            residenceFactory.Setup(factory => factory.Resolve()).Returns(residence.Object);
            var manager = new GlobalResidenceManager(
                Mock.Of<ILogger<GlobalResidenceManager>>(),
                Mock.Of<IRealmContext>(),
                Mock.Of<IDatabaseManager>(),
                Mock.Of<ICharacterManager>(),
                residenceFactory.Object);

            manager.CreateResidence(player.Object);

            Assert.Equal(expectedVisitable, manager.GetRandomVisitableResidences().Any());
        }

        [Fact]
        public void ResidenceManager_PublicReplayKeepsIndexCurrentAndResendsBasics()
        {
            ResidenceManagerFixture fixture = CreateResidenceManagerFixture();

            fixture.Manager.SetResidencePrivacy(ResidencePrivacyLevel.Public);
            fixture.Manager.SetResidencePrivacy(ResidencePrivacyLevel.Public);

            Assert.Equal(ResidencePrivacyLevel.Public, fixture.Residence.Object.PrivacyLevel);
            fixture.GlobalResidenceManager.Verify(manager => manager.RegisterResidenceVists(
                fixture.Residence.Object, "Owner"), Times.Exactly(2));
            fixture.GlobalResidenceManager.Verify(manager => manager.DeregisterResidenceVists(
                It.IsAny<Identity>()), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.Is<ServerHousingBasics>(message =>
                    message.ResidenceId == fixture.Residence.Object.Identity.Id
                    && message.PrivacyLevel == ServerHousingBasics.ResidencePrivacyLevelFlags.Public)), Times.Exactly(2));
        }

        [Fact]
        public void ResidenceManager_PrivateReplayKeepsIndexAbsentAndResendsBasics()
        {
            ResidenceManagerFixture fixture = CreateResidenceManagerFixture();

            fixture.Manager.SetResidencePrivacy(ResidencePrivacyLevel.Private);
            fixture.Manager.SetResidencePrivacy(ResidencePrivacyLevel.Private);

            Assert.Equal(ResidencePrivacyLevel.Private, fixture.Residence.Object.PrivacyLevel);
            fixture.GlobalResidenceManager.Verify(manager => manager.DeregisterResidenceVists(
                fixture.Residence.Object.Identity), Times.Exactly(2));
            fixture.GlobalResidenceManager.Verify(manager => manager.RegisterResidenceVists(
                It.IsAny<IResidence>(), It.IsAny<string>()), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.Is<ServerHousingBasics>(message =>
                    message.ResidenceId == fixture.Residence.Object.Identity.Id
                    && message.PrivacyLevel == ServerHousingBasics.ResidencePrivacyLevelFlags.Private)), Times.Exactly(2));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void ResidenceManager_UnsupportedPrivacyResynchronisesWithoutMutation(int rawPrivacy)
        {
            ResidenceManagerFixture fixture = CreateResidenceManagerFixture();

            fixture.Manager.SetResidencePrivacy((ResidencePrivacyLevel)rawPrivacy);

            Assert.Equal(ResidencePrivacyLevel.Public, fixture.Residence.Object.PrivacyLevel);
            fixture.GlobalResidenceManager.Verify(manager => manager.RegisterResidenceVists(
                It.IsAny<IResidence>(), It.IsAny<string>()), Times.Never);
            fixture.GlobalResidenceManager.Verify(manager => manager.DeregisterResidenceVists(
                It.IsAny<Identity>()), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.Is<ServerHousingBasics>(message =>
                    message.ResidenceId == fixture.Residence.Object.Identity.Id
                    && message.PrivacyLevel == ServerHousingBasics.ResidencePrivacyLevelFlags.Public)), Times.Once);
        }

        [Fact]
        public void ResidenceManager_DifferentResidenceMapResynchronisesWithoutMutation()
        {
            ResidenceManagerFixture fixture = CreateResidenceManagerFixture(useDifferentPlayerMap: true);

            fixture.Manager.SetResidencePrivacy(ResidencePrivacyLevel.Private);

            Assert.Equal(ResidencePrivacyLevel.Public, fixture.Residence.Object.PrivacyLevel);
            fixture.GlobalResidenceManager.Verify(manager => manager.RegisterResidenceVists(
                It.IsAny<IResidence>(), It.IsAny<string>()), Times.Never);
            fixture.GlobalResidenceManager.Verify(manager => manager.DeregisterResidenceVists(
                It.IsAny<Identity>()), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<IWritable>()), Times.Once);
        }

        [Fact]
        public void ResidenceManager_BothMapsNullResynchronisesWithoutMutation()
        {
            ResidenceManagerFixture fixture = CreateResidenceManagerFixture(useNullMaps: true);

            fixture.Manager.SetResidencePrivacy(ResidencePrivacyLevel.Private);

            Assert.Equal(ResidencePrivacyLevel.Public, fixture.Residence.Object.PrivacyLevel);
            fixture.GlobalResidenceManager.Verify(manager => manager.RegisterResidenceVists(
                It.IsAny<IResidence>(), It.IsAny<string>()), Times.Never);
            fixture.GlobalResidenceManager.Verify(manager => manager.DeregisterResidenceVists(
                It.IsAny<Identity>()), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<IWritable>()), Times.Once);
        }

        [Fact]
        public void ResidenceManager_DifferentOwnerResynchronisesWithoutMutation()
        {
            ResidenceManagerFixture fixture = CreateResidenceManagerFixture(useDifferentOwner: true);

            fixture.Manager.SetResidencePrivacy(ResidencePrivacyLevel.Private);

            Assert.Equal(ResidencePrivacyLevel.Public, fixture.Residence.Object.PrivacyLevel);
            fixture.GlobalResidenceManager.Verify(manager => manager.RegisterResidenceVists(
                It.IsAny<IResidence>(), It.IsAny<string>()), Times.Never);
            fixture.GlobalResidenceManager.Verify(manager => manager.DeregisterResidenceVists(
                It.IsAny<Identity>()), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<IWritable>()), Times.Once);
        }

        [Fact]
        public void ResidenceManager_CommunityResidenceResynchronisesWithoutMutation()
        {
            ResidenceManagerFixture fixture = CreateResidenceManagerFixture(ResidenceType.Community);

            fixture.Manager.SetResidencePrivacy(ResidencePrivacyLevel.Private);

            Assert.Equal(ResidencePrivacyLevel.Public, fixture.Residence.Object.PrivacyLevel);
            fixture.GlobalResidenceManager.Verify(manager => manager.RegisterResidenceVists(
                It.IsAny<IResidence>(), It.IsAny<string>()), Times.Never);
            fixture.GlobalResidenceManager.Verify(manager => manager.DeregisterResidenceVists(
                It.IsAny<Identity>()), Times.Never);
            fixture.Session.Verify(session => session.EnqueueMessageEncrypted(
                It.IsAny<IWritable>()), Times.Once);
        }

        private static Mock<IResidence> CreateResidence(Identity identity, ulong ownerId, Action acknowledged)
        {
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(identity);
            residence.SetupGet(value => value.OwnerIdentity).Returns(new Identity
            {
                RealmId = identity.RealmId,
                Id      = ownerId
            });
            residence.SetupGet(value => value.PrivacyLevel).Returns(ResidencePrivacyLevel.Private);
            residence.Setup(value => value.Save(It.IsAny<CharacterContext>(), It.IsAny<ISaveCommitScope>()))
                .Callback<CharacterContext, ISaveCommitScope>((_, scope) => scope.Register(acknowledged));
            return residence;
        }

        private static GlobalResidenceManager CreateManager(
            ICharacterManager characterManager = null,
            IFactory<IResidence> residenceFactory = null)
        {
            return new GlobalResidenceManager(
                Mock.Of<ILogger<GlobalResidenceManager>>(),
                Mock.Of<IRealmContext>(),
                Mock.Of<IDatabaseManager>(),
                characterManager ?? Mock.Of<ICharacterManager>(),
                residenceFactory ?? Mock.Of<IFactory<IResidence>>());
        }

        private static void StorePersistedCommunity(
            GlobalResidenceManager manager,
            IResidence residence,
            ICommunity community)
        {
            MethodInfo method = typeof(GlobalResidenceManager).GetMethod(
                "StoreCommunity",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(manager, [residence, community]);
        }

        private static ResidenceManagerFixture CreateResidenceManagerFixture(
            ResidenceType residenceType = ResidenceType.Residence,
            bool useDifferentOwner = false,
            bool useDifferentPlayerMap = false,
            bool useNullMaps = false)
        {
            Identity playerIdentity = new() { RealmId = 1, Id = 11ul };
            Identity residenceOwnerIdentity = useDifferentOwner
                ? new Identity { RealmId = 1, Id = 12ul }
                : playerIdentity;
            Identity residenceIdentity = new() { RealmId = 1, Id = 21ul };
            Mock<IResidenceMapInstance> residenceMap = useNullMaps
                ? null
                : new Mock<IResidenceMapInstance>();
            Mock<IResidenceMapInstance> playerMap = useNullMaps
                ? null
                : useDifferentPlayerMap
                    ? new Mock<IResidenceMapInstance>()
                    : residenceMap;
            var session = new Mock<IGameSession>();
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            residence.SetupGet(value => value.OwnerIdentity).Returns(residenceOwnerIdentity);
            residence.SetupGet(value => value.Type).Returns(residenceType);
            residence.SetupGet(value => value.Map).Returns(residenceMap?.Object);
            residence.SetupProperty(value => value.PrivacyLevel, ResidencePrivacyLevel.Public);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Identity).Returns(playerIdentity);
            player.SetupGet(value => value.Name).Returns("Owner");
            player.SetupGet(value => value.Map).Returns(playerMap?.Object);
            player.SetupGet(value => value.Session).Returns(session.Object);

            var globalResidenceManager = new Mock<IGlobalResidenceManager>();
            globalResidenceManager.Setup(manager => manager.GetResidenceByOwner(playerIdentity)).Returns(residence.Object);
            var manager = new ResidenceManager(globalResidenceManager.Object);
            manager.Initialise(player.Object);

            return new ResidenceManagerFixture(
                manager,
                globalResidenceManager,
                residence,
                session);
        }

        private sealed record ResidenceManagerFixture(
            ResidenceManager Manager,
            Mock<IGlobalResidenceManager> GlobalResidenceManager,
            Mock<IResidence> Residence,
            Mock<IGameSession> Session);

        private static IPlayer CreatePlayer(string name, ulong characterId)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Name).Returns(name);
            player.SetupGet(value => value.Identity).Returns(new Identity
            {
                RealmId = 1,
                Id      = characterId
            });
            return player.Object;
        }

        private sealed class QueuedCharacterDatabase : CharacterDatabase
        {
            private readonly Queue<CharacterContext> contexts;

            public QueuedCharacterDatabase(params CharacterContext[] contexts)
            {
                this.contexts = new Queue<CharacterContext>(contexts);
            }

            protected override CharacterContext CreateContext()
            {
                return contexts.Dequeue();
            }
        }

        private sealed class ControlledCharacterContext : CharacterContext
        {
            private readonly Task<int> saveTask;

            public ControlledCharacterContext(Task<int> saveTask)
                : base(new DbContextOptionsBuilder<CharacterContext>().Options)
            {
                this.saveTask = saveTask;
            }

            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                return saveTask;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
            }
        }
    }
}
