using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Matching;
using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Matching.Queue;
using NexusForever.Game.Static.Matching;
using NexusForever.Shared;
using MatchingMatchType = NexusForever.Game.Static.Matching.MatchType;

namespace NexusForever.Game.Tests.Matching.Queue
{
    public sealed class MatchingQueueLeaveTests
    {
        [Fact]
        public void LeaveQueue_AbsentCharacterDoesNotAllocateMatchingState()
        {
            var fixture = new Fixture();

            fixture.Manager.LeaveQueue(fixture.Player.Object, MatchingMatchType.Dungeon);

            fixture.Player.VerifyGet(item => item.Identity, Times.Exactly(2));
            fixture.Player.VerifyNoOtherCalls();
            fixture.MatchingCharacterFactory.VerifyNoOtherCalls();
        }

        [Fact]
        public void LeaveAll_AbsentCharacterDoesNotAllocateMatchingState()
        {
            var fixture = new Fixture();

            fixture.Manager.LeaveQueue(fixture.Player.Object);

            fixture.Player.VerifyGet(item => item.Identity, Times.Exactly(2));
            fixture.Player.VerifyNoOtherCalls();
            fixture.MatchingCharacterFactory.VerifyNoOtherCalls();
        }

        [Fact]
        public void LeaveQueue_MissingEntryAndReplayAreNoOps()
        {
            var fixture = new Fixture();
            Mock<IMatchingCharacter> character = fixture.RegisterCharacter();
            character
                .Setup(item => item.GetMatchingCharacterQueue(MatchingMatchType.Dungeon))
                .Returns((IMatchingCharacterQueue)null);

            fixture.Manager.LeaveQueue(fixture.Player.Object, MatchingMatchType.Dungeon);
            fixture.Manager.LeaveQueue(fixture.Player.Object, MatchingMatchType.Dungeon);

            character.Verify(
                item => item.GetMatchingCharacterQueue(MatchingMatchType.Dungeon),
                Times.Exactly(2));
            fixture.VerifyRegisteredCharacter(character);
            fixture.Player.VerifyGet(item => item.Identity, Times.Exactly(4));
            fixture.Player.VerifyNoOtherCalls();
        }

        [Fact]
        public void LeaveQueue_ExistingEntryRemovesExactProposalOnceAndReplayIsNoOp()
        {
            var fixture = new Fixture();
            Mock<IMatchingCharacter> character = fixture.RegisterCharacter();
            var queue = CreateQueue(MatchingMatchType.Dungeon);
            IMatchingCharacterQueue currentQueue = queue.Queue;
            character
                .Setup(item => item.GetMatchingCharacterQueue(MatchingMatchType.Dungeon))
                .Returns(() => currentQueue);
            queue.Group
                .Setup(item => item.RemoveMatchingQueueProposal(
                    queue.Proposal.Object,
                    MatchingQueueResult.Left))
                .Callback(() => currentQueue = null);

            fixture.Manager.LeaveQueue(fixture.Player.Object, MatchingMatchType.Dungeon);
            fixture.Manager.LeaveQueue(fixture.Player.Object, MatchingMatchType.Dungeon);

            queue.Group.Verify(item => item.RemoveMatchingQueueProposal(
                queue.Proposal.Object,
                MatchingQueueResult.Left), Times.Once);
            queue.Group.VerifyNoOtherCalls();
            queue.Proposal.VerifyNoOtherCalls();
            character.Verify(
                item => item.GetMatchingCharacterQueue(MatchingMatchType.Dungeon),
                Times.Exactly(2));
            fixture.VerifyRegisteredCharacter(character);
        }

        [Fact]
        public void LeaveAll_EmptyCharacterAndReplayAreNoOps()
        {
            var fixture = new Fixture();
            Mock<IMatchingCharacter> character = fixture.RegisterCharacter();
            character
                .Setup(item => item.GetMatchingCharacterQueues())
                .Returns(Array.Empty<IMatchingCharacterQueue>());

            fixture.Manager.LeaveQueue(fixture.Player.Object);
            fixture.Manager.LeaveQueue(fixture.Player.Object);

            character.Verify(item => item.GetMatchingCharacterQueues(), Times.Exactly(2));
            fixture.VerifyRegisteredCharacter(character);
        }

        [Fact]
        public void LeaveAll_SnapshotsBeforeRemovalAndSkipsReplacedQueueRecord()
        {
            var fixture = new Fixture();
            Mock<IMatchingCharacter> character = fixture.RegisterCharacter();
            var first = CreateQueue(MatchingMatchType.Dungeon);
            var stale = CreateQueue(MatchingMatchType.Adventure);
            var last = CreateQueue(MatchingMatchType.Arena);
            var replacement = CreateQueue(MatchingMatchType.Adventure);
            var source = new List<IMatchingCharacterQueue>
            {
                first.Queue,
                stale.Queue,
                last.Queue
            };
            var currentQueues = new Dictionary<MatchingMatchType, IMatchingCharacterQueue>
            {
                [MatchingMatchType.Dungeon]   = first.Queue,
                [MatchingMatchType.Adventure] = stale.Queue,
                [MatchingMatchType.Arena]     = last.Queue
            };
            var removed = new List<MatchingMatchType>();

            character
                .Setup(item => item.GetMatchingCharacterQueues())
                .Returns(source);
            character
                .Setup(item => item.GetMatchingCharacterQueue(It.IsAny<MatchingMatchType>()))
                .Returns((MatchingMatchType matchType) => currentQueues.TryGetValue(matchType, out IMatchingCharacterQueue queue)
                    ? queue
                    : null);
            first.Group
                .Setup(item => item.RemoveMatchingQueueProposal(
                    first.Proposal.Object,
                    MatchingQueueResult.Left))
                .Callback(() =>
                {
                    removed.Add(MatchingMatchType.Dungeon);
                    source.Clear();
                    currentQueues[MatchingMatchType.Adventure] = replacement.Queue;
                });
            last.Group
                .Setup(item => item.RemoveMatchingQueueProposal(
                    last.Proposal.Object,
                    MatchingQueueResult.Left))
                .Callback(() => removed.Add(MatchingMatchType.Arena));

            fixture.Manager.LeaveQueue(fixture.Player.Object);

            Assert.Equal(
                new[] { MatchingMatchType.Dungeon, MatchingMatchType.Arena },
                removed);
            first.Group.Verify(item => item.RemoveMatchingQueueProposal(
                first.Proposal.Object,
                MatchingQueueResult.Left), Times.Once);
            last.Group.Verify(item => item.RemoveMatchingQueueProposal(
                last.Proposal.Object,
                MatchingQueueResult.Left), Times.Once);
            stale.Group.VerifyNoOtherCalls();
            replacement.Group.VerifyNoOtherCalls();
            character.Verify(item => item.GetMatchingCharacterQueues(), Times.Once);
            character.Verify(
                item => item.GetMatchingCharacterQueue(It.IsAny<MatchingMatchType>()),
                Times.Exactly(3));
            fixture.VerifyRegisteredCharacter(character);
        }

        private static QueueFixture CreateQueue(MatchingMatchType matchType)
        {
            var proposal = new Mock<IMatchingQueueProposal>(MockBehavior.Strict);
            proposal.SetupGet(item => item.MatchType).Returns(matchType);
            var group = new Mock<IMatchingQueueGroup>(MockBehavior.Strict);
            return new QueueFixture(
                new MatchingCharacterQueue
                {
                    MatchingQueueProposal = proposal.Object,
                    MatchingQueueGroup    = group.Object
                },
                proposal,
                group);
        }

        private sealed record QueueFixture(
            MatchingCharacterQueue Queue,
            Mock<IMatchingQueueProposal> Proposal,
            Mock<IMatchingQueueGroup> Group);

        private sealed class Fixture
        {
            public Identity Identity { get; } = new()
            {
                Id      = 101ul,
                RealmId = 1
            };

            public Mock<IPlayer> Player { get; } = new(MockBehavior.Strict);
            public Mock<IFactory<IMatchingCharacter>> MatchingCharacterFactory { get; } = new(MockBehavior.Strict);
            public MatchingManager Manager { get; }

            public Fixture()
            {
                Player.SetupGet(item => item.Identity).Returns(Identity);
                Manager = new MatchingManager(
                    NullLogger<MatchingManager>.Instance,
                    new Mock<IMatchingDataManager>(MockBehavior.Strict).Object,
                    new Mock<IMatchingQueueTimeManager>(MockBehavior.Strict).Object,
                    new Mock<IFactory<IMatchingQueueManager>>(MockBehavior.Strict).Object,
                    new Mock<IFactory<IMatchingQueueProposal>>(MockBehavior.Strict).Object,
                    new Mock<IFactory<IMatchingRoleCheck>>(MockBehavior.Strict).Object,
                    MatchingCharacterFactory.Object);
            }

            public Mock<IMatchingCharacter> RegisterCharacter()
            {
                var character = new Mock<IMatchingCharacter>(MockBehavior.Strict);
                MatchingCharacterFactory
                    .Setup(item => item.Resolve())
                    .Returns(character.Object);
                character
                    .Setup(item => item.Initialise(Identity));

                Assert.Same(character.Object, Manager.GetMatchingCharacter(Identity));
                return character;
            }

            public void VerifyRegisteredCharacter(Mock<IMatchingCharacter> character)
            {
                MatchingCharacterFactory.Verify(item => item.Resolve(), Times.Once);
                character.Verify(item => item.Initialise(Identity), Times.Once);
                MatchingCharacterFactory.VerifyNoOtherCalls();
                character.VerifyNoOtherCalls();
            }
        }
    }
}
