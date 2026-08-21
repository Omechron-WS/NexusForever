using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Matching;
using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Matching.Queue;
using NexusForever.Game.Static.Matching;
using NexusForever.Game.Static.Reputation;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model;
using NexusForever.Shared;
using MatchingMatchType = NexusForever.Game.Static.Matching.MatchType;

namespace NexusForever.Game.Tests.Matching.Queue
{
    public sealed class MatchingMapAdmissionTests
    {
        [Fact]
        public void JoinQueue_UnknownExplicitMapReturnsInvalidGameBeforeProposalConstruction()
        {
            var fixture = new Fixture();
            fixture.MatchingDataManager
                .Setup(value => value.GetMatchingMap(999u))
                .Returns((IMatchingMap)null);

            fixture.Manager.JoinQueue(
                fixture.Player.Object,
                Role.DPS,
                MatchingMatchType.Dungeon,
                [999u],
                0u,
                MatchingQueueFlags.None);

            fixture.VerifyInvalidGame();
            fixture.MatchingDataManager.Verify(
                value => value.GetMatchingMap(999u),
                Times.Once);
            fixture.MatchingDataManager.VerifyNoOtherCalls();
            fixture.MatchingQueueProposalFactory.VerifyNoOtherCalls();
        }

        [Fact]
        public void JoinPartyQueue_DuplicateExplicitMapReturnsInvalidGameBeforeCatalogOrPartyWork()
        {
            var fixture = new Fixture();

            fixture.Manager.JoinPartyQueue(
                fixture.Player.Object,
                Role.DPS,
                MatchingMatchType.Dungeon,
                [11u, 11u],
                0u,
                MatchingQueueFlags.None);

            fixture.VerifyInvalidGame();
            fixture.MatchingDataManager.VerifyNoOtherCalls();
            fixture.MatchingQueueProposalFactory.VerifyNoOtherCalls();
            fixture.MatchingRoleCheckFactory.VerifyNoOtherCalls();
            fixture.Player.VerifyGet(value => value.Faction1, Times.Never);
            fixture.Player.VerifyGet(value => value.Map, Times.Never);
            fixture.Player.VerifyGet(value => value.Position, Times.Never);
        }

        [Fact]
        public void JoinQueue_ValidDistinctExplicitMapsPreservePacketOrder()
        {
            var fixture = new Fixture();
            Mock<IMatchingMap> firstMap = fixture.AddExplicitMap(11u);
            Mock<IMatchingMap> secondMap = fixture.AddExplicitMap(27u);
            fixture.SetupSoloProposal(
                MatchingMatchType.Dungeon,
                Role.DPS,
                MatchingQueueFlags.None);

            fixture.Manager.JoinQueue(
                fixture.Player.Object,
                Role.DPS,
                MatchingMatchType.Dungeon,
                [11u, 27u],
                0u,
                MatchingQueueFlags.None);

            Assert.Collection(
                fixture.InitialisedMaps,
                value => Assert.Same(firstMap.Object, value),
                value => Assert.Same(secondMap.Object, value));
            fixture.MatchingDataManager.Verify(
                value => value.GetMatchingMap(11u),
                Times.Once);
            fixture.MatchingDataManager.Verify(
                value => value.GetMatchingMap(27u),
                Times.Once);
            fixture.VerifySoloProposal(
                MatchingMatchType.Dungeon,
                Role.DPS,
                MatchingQueueFlags.None);
            fixture.Session.Verify(
                value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Never);
        }

        [Fact]
        public void JoinQueue_NonzeroGameTypePreservesServerCatalogBranch()
        {
            var fixture = new Fixture();
            var firstMap = new Mock<IMatchingMap>(MockBehavior.Strict);
            var secondMap = new Mock<IMatchingMap>(MockBehavior.Strict);
            fixture.MatchingDataManager
                .Setup(value => value.GetMatchingMaps(3u))
                .Returns(new[] { firstMap.Object, secondMap.Object });
            fixture.SetupSoloProposal(
                MatchingMatchType.Arena,
                Role.DPS,
                MatchingQueueFlags.None);

            fixture.Manager.JoinQueue(
                fixture.Player.Object,
                Role.DPS,
                MatchingMatchType.Arena,
                [999u, 999u],
                3u,
                MatchingQueueFlags.None);

            Assert.Collection(
                fixture.InitialisedMaps,
                value => Assert.Same(firstMap.Object, value),
                value => Assert.Same(secondMap.Object, value));
            fixture.MatchingDataManager.Verify(
                value => value.GetMatchingMaps(3u),
                Times.Once);
            fixture.MatchingDataManager.Verify(
                value => value.GetMatchingMap(It.IsAny<uint>()),
                Times.Never);
            fixture.VerifySoloProposal(
                MatchingMatchType.Arena,
                Role.DPS,
                MatchingQueueFlags.None);
            fixture.Session.Verify(
                value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Never);
        }

        [Fact]
        public void JoinQueue_EmptyExplicitMapListPreservesExistingProposalFlow()
        {
            var fixture = new Fixture();
            fixture.SetupSoloProposal(
                MatchingMatchType.Dungeon,
                Role.DPS,
                MatchingQueueFlags.None);

            fixture.Manager.JoinQueue(
                fixture.Player.Object,
                Role.DPS,
                MatchingMatchType.Dungeon,
                [],
                0u,
                MatchingQueueFlags.None);

            Assert.Empty(fixture.InitialisedMaps);
            fixture.MatchingDataManager.VerifyNoOtherCalls();
            fixture.VerifySoloProposal(
                MatchingMatchType.Dungeon,
                Role.DPS,
                MatchingQueueFlags.None);
            fixture.Session.Verify(
                value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Never);
        }

        private sealed class Fixture
        {
            public Identity Identity { get; } = new()
            {
                RealmId = 1,
                Id = 1ul
            };

            public Mock<IMatchingDataManager> MatchingDataManager { get; } = new(MockBehavior.Strict);
            public Mock<IFactory<IMatchingQueueProposal>> MatchingQueueProposalFactory { get; } = new(MockBehavior.Strict);
            public Mock<IFactory<IMatchingRoleCheck>> MatchingRoleCheckFactory { get; } = new(MockBehavior.Strict);
            public Mock<IPlayer> Player { get; } = new(MockBehavior.Strict);
            public Mock<IGameSession> Session { get; } = new(MockBehavior.Strict);
            public MatchingManager Manager { get; }

            public IReadOnlyList<IMatchingMap> InitialisedMaps { get; private set; }

            private readonly Mock<IMatchingQueueProposal> matchingQueueProposal = new(MockBehavior.Strict);

            public Fixture()
            {
                Player.SetupGet(value => value.Identity).Returns(Identity);
                Player.SetupGet(value => value.Session).Returns(Session.Object);
                Session.Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()));

                Manager = new MatchingManager(
                    NullLogger<MatchingManager>.Instance,
                    MatchingDataManager.Object,
                    new Mock<IMatchingQueueTimeManager>(MockBehavior.Strict).Object,
                    new Mock<IFactory<IMatchingQueueManager>>(MockBehavior.Strict).Object,
                    MatchingQueueProposalFactory.Object,
                    MatchingRoleCheckFactory.Object,
                    new Mock<IFactory<IMatchingCharacter>>(MockBehavior.Strict).Object);
            }

            public Mock<IMatchingMap> AddExplicitMap(uint id)
            {
                var matchingMap = new Mock<IMatchingMap>(MockBehavior.Strict);
                MatchingDataManager
                    .Setup(value => value.GetMatchingMap(id))
                    .Returns(matchingMap.Object);
                return matchingMap;
            }

            public void SetupSoloProposal(
                MatchingMatchType matchType,
                Role roles,
                MatchingQueueFlags matchingQueueFlags)
            {
                Player.SetupGet(value => value.Faction1).Returns(Faction.Exile);
                MatchingQueueProposalFactory
                    .Setup(value => value.Resolve())
                    .Returns(matchingQueueProposal.Object);
                matchingQueueProposal
                    .Setup(value => value.Initialise(
                        Faction.Exile,
                        matchType,
                        It.IsAny<IEnumerable<IMatchingMap>>(),
                        matchingQueueFlags))
                    .Callback<Faction, MatchingMatchType, IEnumerable<IMatchingMap>, MatchingQueueFlags>(
                        (_, _, maps, _) => InitialisedMaps = maps.ToList());
                matchingQueueProposal
                    .Setup(value => value.AddMember(Identity, roles));
            }

            public void VerifySoloProposal(
                MatchingMatchType matchType,
                Role roles,
                MatchingQueueFlags matchingQueueFlags)
            {
                matchingQueueProposal.Verify(
                    value => value.Initialise(
                        Faction.Exile,
                        matchType,
                        It.IsAny<IEnumerable<IMatchingMap>>(),
                        matchingQueueFlags),
                    Times.Once);
                matchingQueueProposal.Verify(
                    value => value.AddMember(Identity, roles),
                    Times.Once);
            }

            public void VerifyInvalidGame()
            {
                Session.Verify(
                    value => value.EnqueueMessageEncrypted(It.Is<IWritable>(message =>
                        message.GetType() == typeof(ServerMatchingQueueResultAnnounce)
                        && ((ServerMatchingQueueResultAnnounce)message).Result == MatchingQueueResult.InvalidGame)),
                    Times.Once);
            }
        }
    }
}
