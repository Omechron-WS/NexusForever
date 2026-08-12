using Microsoft.Extensions.Logging;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.PublicEvent;
using NexusForever.Network.Message;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.PublicEvent
{
    public sealed class PublicEventTeamBroadcastTests
    {
        [Fact]
        public void MemberFailure_DoesNotSuppressLaterMemberAndIsRetriedByNextBroadcast()
        {
            var operations = new List<string>();
            IWritable message = Mock.Of<IWritable>();
            int failedAttempts = 0;
            Mock<IPublicEventTeamMember> failedMember = CreateMember(1ul);
            failedMember
                .Setup(member => member.Send(message))
                .Callback(() =>
                {
                    operations.Add("failed");
                    if (failedAttempts++ == 0)
                        throw new InvalidOperationException("Test member send failure.");
                });
            Mock<IPublicEventTeamMember> healthyMember = CreateMember(2ul);
            healthyMember
                .Setup(member => member.Send(message))
                .Callback(() => operations.Add("healthy"));
            PublicEventTeam team = CreateTeam(failedMember, healthyMember);
            JoinTeam(team, 1ul);
            JoinTeam(team, 2ul);

            Exception firstException = Record.Exception(() => team.Broadcast(message));
            Exception secondException = Record.Exception(() => team.Broadcast(message));

            Assert.Null(firstException);
            Assert.Null(secondException);
            Assert.Equal(["failed", "healthy", "failed", "healthy"], operations);
            failedMember.Verify(member => member.Send(message), Times.Exactly(2));
            healthyMember.Verify(member => member.Send(message), Times.Exactly(2));
            Assert.Collection(
                team.GetMembers(),
                member => Assert.Same(failedMember.Object, member),
                member => Assert.Same(healthyMember.Object, member));
        }

        [Fact]
        public void RemovedSnapshotMember_IsSkippedAndDistinctAdditionWaitsForNextBroadcast()
        {
            var operations = new List<string>();
            IWritable message = Mock.Of<IWritable>();
            Mock<IPublicEventTeamMember> firstMember = CreateMember(1ul);
            Mock<IPublicEventTeamMember> removedMember = CreateMember(2ul);
            Mock<IPublicEventTeamMember> addedMember = CreateMember(3ul);
            PublicEventTeam team = CreateTeam(firstMember, removedMember, addedMember);
            bool membershipChanged = false;
            firstMember
                .Setup(member => member.Send(message))
                .Callback(() =>
                {
                    operations.Add("first");
                    if (membershipChanged)
                        return;

                    membershipChanged = true;
                    team.LeaveTeam(2ul);
                    JoinTeam(team, 3ul);
                });
            removedMember
                .Setup(member => member.Send(message))
                .Callback(() => operations.Add("removed"));
            addedMember
                .Setup(member => member.Send(message))
                .Callback(() => operations.Add("added"));
            JoinTeam(team, 1ul);
            JoinTeam(team, 2ul);

            team.Broadcast(message);

            Assert.Equal(["first"], operations);
            removedMember.Verify(member => member.Send(It.IsAny<IWritable>()), Times.Never);
            addedMember.Verify(member => member.Send(It.IsAny<IWritable>()), Times.Never);

            team.Broadcast(message);

            Assert.Equal(["first", "first", "added"], operations);
            addedMember.Verify(member => member.Send(message), Times.Once);
        }

        [Fact]
        public void ReplacedSnapshotMember_IsSkippedByExactReferenceUntilNextBroadcast()
        {
            var operations = new List<string>();
            IWritable message = Mock.Of<IWritable>();
            Mock<IPublicEventTeamMember> firstMember = CreateMember(1ul);
            Mock<IPublicEventTeamMember> replacedMember = CreateMember(2ul);
            Mock<IPublicEventTeamMember> replacementMember = CreateMember(2ul);
            PublicEventTeam team = CreateTeam(firstMember, replacedMember, replacementMember);
            bool memberReplaced = false;
            firstMember
                .Setup(member => member.Send(message))
                .Callback(() =>
                {
                    operations.Add("first");
                    if (memberReplaced)
                        return;

                    memberReplaced = true;
                    team.LeaveTeam(2ul);
                    JoinTeam(team, 2ul);
                });
            replacedMember
                .Setup(member => member.Send(message))
                .Callback(() => operations.Add("replaced"));
            replacementMember
                .Setup(member => member.Send(message))
                .Callback(() => operations.Add("replacement"));
            JoinTeam(team, 1ul);
            JoinTeam(team, 2ul);

            team.Broadcast(message);

            Assert.Equal(["first"], operations);
            replacedMember.Verify(member => member.Send(It.IsAny<IWritable>()), Times.Never);
            replacementMember.Verify(member => member.Send(It.IsAny<IWritable>()), Times.Never);

            team.Broadcast(message);

            Assert.Equal(["first", "first", "replacement"], operations);
            replacementMember.Verify(member => member.Send(message), Times.Once);
            Assert.Collection(
                team.GetMembers(),
                member => Assert.Same(firstMember.Object, member),
                member => Assert.Same(replacementMember.Object, member));
        }

        [Fact]
        public void DistinctAddition_PreservesSnapshotOrderAndWaitsForNextBroadcast()
        {
            var operations = new List<string>();
            IWritable message = Mock.Of<IWritable>();
            Mock<IPublicEventTeamMember> firstMember = CreateMember(1ul);
            Mock<IPublicEventTeamMember> secondMember = CreateMember(2ul);
            Mock<IPublicEventTeamMember> addedMember = CreateMember(3ul);
            PublicEventTeam team = CreateTeam(firstMember, secondMember, addedMember);
            bool memberAdded = false;
            firstMember
                .Setup(member => member.Send(message))
                .Callback(() =>
                {
                    operations.Add("first");
                    if (memberAdded)
                        return;

                    memberAdded = true;
                    JoinTeam(team, 3ul);
                });
            secondMember
                .Setup(member => member.Send(message))
                .Callback(() => operations.Add("second"));
            addedMember
                .Setup(member => member.Send(message))
                .Callback(() => operations.Add("added"));
            JoinTeam(team, 1ul);
            JoinTeam(team, 2ul);

            team.Broadcast(message);

            Assert.Equal(["first", "second"], operations);
            addedMember.Verify(member => member.Send(It.IsAny<IWritable>()), Times.Never);

            team.Broadcast(message);

            Assert.Equal(["first", "second", "first", "second", "added"], operations);
            addedMember.Verify(member => member.Send(message), Times.Once);
        }

        private static PublicEventTeam CreateTeam(params Mock<IPublicEventTeamMember>[] members)
        {
            var pendingMembers = new Queue<IPublicEventTeamMember>(members.Select(member => member.Object));
            var memberFactory = new Mock<IFactory<IPublicEventTeamMember>>();
            memberFactory
                .Setup(factory => factory.Resolve())
                .Returns(() => pendingMembers.Dequeue());
            return new PublicEventTeam(
                Mock.Of<ILogger<PublicEventTeam>>(),
                Mock.Of<IFactory<IPublicEventObjective>>(),
                memberFactory.Object,
                Mock.Of<IFactory<IPublicEventVote>>(),
                Mock.Of<IPublicEventStats>());
        }

        private static Mock<IPublicEventTeamMember> CreateMember(ulong characterId)
        {
            var member = new Mock<IPublicEventTeamMember>();
            member.SetupGet(value => value.CharacterId).Returns(characterId);
            return member;
        }

        private static void JoinTeam(PublicEventTeam team, ulong characterId)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(characterId);
            team.JoinTeam(player.Object);
        }
    }
}
