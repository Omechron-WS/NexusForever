using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexusForever.Database;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Matching;
using NexusForever.Game.Abstract.Matching.Match;
using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Matching;
using NexusForever.Game.Matching.Queue;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Matching;
using NexusForever.Game.Static.Reputation;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Shared;
using NexusForever.GameTable;
using MatchingMatchType = NexusForever.Game.Static.Matching.MatchType;

namespace NexusForever.Game.Tests.Matching.Queue
{
    public sealed class MatchingRoleAdmissionTests
    {
        public static TheoryData<Class, Role, Role> IneligibleRoleMasks => new()
        {
            { Class.Warrior, Role.Tank | Role.DPS, Role.Healer },
            { Class.Engineer, Role.Tank | Role.DPS, Role.Healer },
            { Class.Stalker, Role.Tank | Role.DPS, Role.Healer },
            { Class.Esper, Role.Healer | Role.DPS, Role.Tank },
            { Class.Medic, Role.Healer | Role.DPS, Role.Tank },
            { Class.Spellslinger, Role.Healer | Role.DPS, Role.Tank }
        };

        public static TheoryData<Class, Role, Role> EligibleRoleMasks => new()
        {
            { Class.Warrior, Role.Tank | Role.DPS, Role.None },
            { Class.Warrior, Role.Tank | Role.DPS, Role.Tank },
            { Class.Warrior, Role.Tank | Role.DPS, Role.DPS },
            { Class.Warrior, Role.Tank | Role.DPS, Role.Tank | Role.DPS },
            { Class.Esper, Role.Healer | Role.DPS, Role.None },
            { Class.Esper, Role.Healer | Role.DPS, Role.Healer },
            { Class.Esper, Role.Healer | Role.DPS, Role.DPS },
            { Class.Esper, Role.Healer | Role.DPS, Role.Healer | Role.DPS }
        };

        public static TheoryData<Role> ReservedRoleMasks => new()
        {
            (Role)0x8,
            unchecked((Role)0x80000000u)
        };

        public static TheoryData<Class, Role> Build16042ClassRoleMasks => new()
        {
            { Class.Warrior, Role.Tank | Role.DPS },
            { Class.Engineer, Role.Tank | Role.DPS },
            { Class.Stalker, Role.Tank | Role.DPS },
            { Class.Esper, Role.Healer | Role.DPS },
            { Class.Medic, Role.Healer | Role.DPS },
            { Class.Spellslinger, Role.Healer | Role.DPS },
            { Class.None, Role.None },
            { (Class)6, Role.None },
            { Class.PvpTeam, Role.None },
            { (Class)byte.MaxValue, Role.None }
        };

        [Theory]
        [MemberData(nameof(Build16042ClassRoleMasks))]
        public void MatchingDataManager_GetDefaultRoleMatchesBuild16042ClassMask(
            Class playerClass,
            Role expectedRoles)
        {
            var gameTableManager = new Mock<IGameTableManager>(MockBehavior.Strict);
            var databaseManager = new Mock<IDatabaseManager>(MockBehavior.Strict);
            var manager = new MatchingDataManager(
                gameTableManager.Object,
                databaseManager.Object);

            Role roles = manager.GetDefaultRole(playerClass);

            Assert.Equal(expectedRoles, roles);
            gameTableManager.VerifyNoOtherCalls();
            databaseManager.VerifyNoOtherCalls();
        }

        [Fact]
        public void QueueValidator_OfflineMemberFailsBeforeRoleOrQueueStateDependencies()
        {
            var fixture = new ValidatorFixture();
            Mock<IPlayer> onlinePlayer = fixture.AddOnlineMember(
                Class.Warrior,
                Role.Tank | Role.DPS,
                Role.Tank);
            fixture.AddOfflineMember(Role.Healer);

            MatchingQueueResult? result = fixture.Validate();

            Assert.Equal(MatchingQueueResult.OfflineGroupMember, result);
            onlinePlayer.VerifyGet(value => value.Class, Times.Never);
            fixture.MatchingDataManager.Verify(
                value => value.GetDefaultRole(It.IsAny<Class>()),
                Times.Never);
            fixture.AssertNoQueueStateAccess();
        }

        [Theory]
        [MemberData(nameof(ReservedRoleMasks))]
        public void QueueValidator_ReservedRoleBitFailsBeforeQueueStateDependencies(Role submittedRoles)
        {
            var fixture = new ValidatorFixture();
            fixture.AddOnlineMember(
                Class.Warrior,
                Role.Tank | Role.DPS,
                submittedRoles);

            MatchingQueueResult? result = fixture.Validate();

            Assert.Equal(MatchingQueueResult.Role, result);
            fixture.AssertNoQueueStateAccess();
        }

        [Theory]
        [MemberData(nameof(IneligibleRoleMasks))]
        public void QueueValidator_CrossClassRoleFailsBeforeQueueStateDependencies(
            Class playerClass,
            Role eligibleRoles,
            Role submittedRoles)
        {
            var fixture = new ValidatorFixture();
            fixture.AddOnlineMember(playerClass, eligibleRoles, submittedRoles);

            MatchingQueueResult? result = fixture.Validate();

            Assert.Equal(MatchingQueueResult.Role, result);
            fixture.AssertNoQueueStateAccess();
        }

        [Fact]
        public void QueueValidator_InvalidLaterMemberRejectsWholeProposalBeforeQueueStateDependencies()
        {
            var fixture = new ValidatorFixture();
            fixture.AddOnlineMember(
                Class.Warrior,
                Role.Tank | Role.DPS,
                Role.Tank);
            fixture.AddOnlineMember(
                Class.Esper,
                Role.Healer | Role.DPS,
                Role.Tank);

            MatchingQueueResult? result = fixture.Validate();

            Assert.Equal(MatchingQueueResult.Role, result);
            fixture.AssertNoQueueStateAccess();
        }

        [Theory]
        [MemberData(nameof(EligibleRoleMasks))]
        public void QueueValidator_EligibleRoleMaskPreservesExistingQueueStateAdmission(
            Class playerClass,
            Role eligibleRoles,
            Role submittedRoles)
        {
            var fixture = new ValidatorFixture();
            Identity identity = fixture.AddOnlineMember(
                playerClass,
                eligibleRoles,
                submittedRoles).Object.Identity;
            var match = new Mock<IMatch>(MockBehavior.Strict);
            var matchCharacter = new Mock<IMatchCharacter>(MockBehavior.Strict);
            matchCharacter.SetupGet(value => value.Match).Returns(match.Object);
            fixture.MatchManager
                .Setup(value => value.GetMatchCharacter(identity))
                .Returns(matchCharacter.Object);

            MatchingQueueResult? result = fixture.Validate();

            Assert.Equal(MatchingQueueResult.InGame, result);
            fixture.MatchManager.Verify(
                value => value.GetMatchCharacter(identity),
                Times.Once);
            fixture.MatchingManager.VerifyNoOtherCalls();
            fixture.MatchingRoleEnforcer.VerifyNoOtherCalls();
            fixture.DisableManager.VerifyNoOtherCalls();
        }

        [Fact]
        public void QueueValidator_ValidDungeonCompositionStillReachesRoleEnforcer()
        {
            var fixture = new ValidatorFixture();
            fixture.AddOnlineMember(Class.Warrior, Role.Tank | Role.DPS, Role.Tank);
            fixture.AddOnlineMember(Class.Esper, Role.Healer | Role.DPS, Role.Healer);
            fixture.AddOnlineMember(Class.Engineer, Role.Tank | Role.DPS, Role.DPS);
            fixture.AddOnlineMember(Class.Medic, Role.Healer | Role.DPS, Role.DPS);
            fixture.AddOnlineMember(Class.Stalker, Role.Tank | Role.DPS, Role.DPS);
            fixture.SetupExistingAdmissionThroughComposition();

            MatchingQueueResult? result = fixture.Validate();

            Assert.Equal(MatchingQueueResult.GroupMemberMatching, result);
            fixture.MatchingRoleEnforcer.Verify(
                value => value.Check(It.IsAny<IEnumerable<IMatchingQueueProposalMember>>()),
                Times.Once);
            fixture.Proposal.Verify(value => value.GetMatchingMaps(), Times.Once);
            fixture.DisableManager.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(Class.Warrior, Role.Healer)]
        [InlineData(Class.Esper, Role.Tank)]
        [InlineData(Class.Warrior, (Role)0x8)]
        public void RoleCheck_IneligibleRoleBecomesExistingRoleDecline(
            Class playerClass,
            Role submittedRoles)
        {
            var fixture = new RoleCheckFixture();
            fixture.SetupOnlinePlayer(playerClass, GetEligibleRoles(playerClass));

            fixture.RoleCheck.Respond(fixture.Identity, submittedRoles);

            Assert.Equal(MatchingRoleCheckStatus.Declined, fixture.RoleCheck.Status);
            Assert.Equal<Role?>(Role.None, fixture.StoredRoles);
            fixture.VerifyRoleDeclineBroadcast();
        }

        [Fact]
        public void RoleCheck_OfflineNonzeroResponseBecomesExistingRoleDecline()
        {
            var fixture = new RoleCheckFixture();
            fixture.PlayerManager
                .Setup(value => value.GetPlayer(fixture.Identity))
                .Returns((IPlayer)null);

            fixture.RoleCheck.Respond(fixture.Identity, Role.DPS);

            Assert.Equal(MatchingRoleCheckStatus.Declined, fixture.RoleCheck.Status);
            Assert.Equal<Role?>(Role.None, fixture.StoredRoles);
            fixture.MatchingDataManager.Verify(
                value => value.GetDefaultRole(It.IsAny<Class>()),
                Times.Never);
            fixture.VerifyRoleDeclineBroadcast();
        }

        [Fact]
        public void RoleCheck_ExplicitNonePreservesDeclineWithoutPlayerLookup()
        {
            var fixture = new RoleCheckFixture();

            fixture.RoleCheck.Respond(fixture.Identity, Role.None);

            Assert.Equal(MatchingRoleCheckStatus.Declined, fixture.RoleCheck.Status);
            Assert.Equal<Role?>(Role.None, fixture.StoredRoles);
            fixture.PlayerManager.VerifyNoOtherCalls();
            fixture.MatchingDataManager.VerifyNoOtherCalls();
            fixture.VerifyRoleDeclineBroadcast();
        }

        [Theory]
        [InlineData(Class.Warrior, Role.Tank | Role.DPS)]
        [InlineData(Class.Esper, Role.Healer | Role.DPS)]
        public void RoleCheck_EligibleCompositePreservesExistingSuccess(
            Class playerClass,
            Role submittedRoles)
        {
            var fixture = new RoleCheckFixture();
            fixture.SetupOnlinePlayer(playerClass, GetEligibleRoles(playerClass));

            fixture.RoleCheck.Respond(fixture.Identity, submittedRoles);

            Assert.Equal(MatchingRoleCheckStatus.Success, fixture.RoleCheck.Status);
            Assert.Equal<Role?>(submittedRoles, fixture.StoredRoles);
            fixture.Member.Verify(
                value => value.Send(It.IsAny<IWritable>()),
                Times.Never);
        }

        [Fact]
        public void RoleCheck_DuplicateResponseRejectsBeforePlayerOrClassLookup()
        {
            var fixture = new RoleCheckFixture(Role.DPS);

            Assert.Throws<InvalidOperationException>(() =>
                fixture.RoleCheck.Respond(fixture.Identity, Role.DPS));

            Assert.Equal(MatchingRoleCheckStatus.Pending, fixture.RoleCheck.Status);
            Assert.Equal<Role?>(Role.DPS, fixture.StoredRoles);
            fixture.PlayerManager.VerifyNoOtherCalls();
            fixture.MatchingDataManager.VerifyNoOtherCalls();
            fixture.Member.Verify(
                value => value.SetRoles(It.IsAny<Role>()),
                Times.Never);
            fixture.Member.Verify(
                value => value.Send(It.IsAny<IWritable>()),
                Times.Never);
        }

        private static Role GetEligibleRoles(Class playerClass)
        {
            return playerClass switch
            {
                Class.Warrior or Class.Engineer or Class.Stalker => Role.Tank | Role.DPS,
                Class.Esper or Class.Medic or Class.Spellslinger => Role.Healer | Role.DPS,
                _ => Role.None
            };
        }

        private sealed class ValidatorFixture
        {
            public Mock<IDisableManager> DisableManager { get; } = new(MockBehavior.Strict);
            public Mock<IMatchManager> MatchManager { get; } = new(MockBehavior.Strict);
            public Mock<IMatchingDataManager> MatchingDataManager { get; } = new(MockBehavior.Strict);
            public Mock<IMatchingManager> MatchingManager { get; } = new(MockBehavior.Strict);
            public Mock<IMatchingQueueProposal> Proposal { get; } = new(MockBehavior.Strict);
            public Mock<IMatchingRoleEnforcer> MatchingRoleEnforcer { get; } = new(MockBehavior.Strict);
            public Mock<IPlayerManager> PlayerManager { get; } = new(MockBehavior.Strict);

            private readonly List<Mock<IMatchingQueueProposalMember>> members = [];
            private readonly List<Mock<IPlayer>> players = [];
            private readonly MatchingQueueValidator validator;
            private ulong nextCharacterId = 1ul;

            public ValidatorFixture()
            {
                validator = new MatchingQueueValidator(
                    PlayerManager.Object,
                    DisableManager.Object,
                    MatchingManager.Object,
                    MatchingDataManager.Object,
                    MatchingRoleEnforcer.Object,
                    MatchManager.Object);
            }

            public Mock<IPlayer> AddOnlineMember(
                Class playerClass,
                Role eligibleRoles,
                Role submittedRoles)
            {
                Identity identity = CreateIdentity();
                AddMember(identity, submittedRoles);

                var player = new Mock<IPlayer>(MockBehavior.Strict);
                player.SetupGet(value => value.Identity).Returns(identity);
                player.SetupGet(value => value.Class).Returns(playerClass);
                players.Add(player);

                PlayerManager
                    .Setup(value => value.GetPlayer(identity))
                    .Returns(player.Object);
                MatchingDataManager
                    .Setup(value => value.GetDefaultRole(playerClass))
                    .Returns(eligibleRoles);
                return player;
            }

            public void AddOfflineMember(Role submittedRoles)
            {
                Identity identity = CreateIdentity();
                AddMember(identity, submittedRoles);
                PlayerManager
                    .Setup(value => value.GetPlayer(identity))
                    .Returns((IPlayer)null);
            }

            public MatchingQueueResult? Validate()
            {
                Proposal
                    .Setup(value => value.GetMembers())
                    .Returns(members.Select(value => value.Object).ToList());
                return validator.CanQueue(Proposal.Object);
            }

            public void AssertNoQueueStateAccess()
            {
                MatchManager.Verify(
                    value => value.GetMatchCharacter(It.IsAny<Identity>()),
                    Times.Never);
                MatchingManager.Verify(
                    value => value.GetMatchingCharacter(It.IsAny<Identity>()),
                    Times.Never);
                MatchingRoleEnforcer.Verify(
                    value => value.Check(It.IsAny<IEnumerable<IMatchingQueueProposalMember>>()),
                    Times.Never);
                Proposal.Verify(
                    value => value.GetMatchingMaps(),
                    Times.Never);
                DisableManager.VerifyNoOtherCalls();
            }

            public void SetupExistingAdmissionThroughComposition()
            {
                const MatchingMatchType matchType = MatchingMatchType.Dungeon;
                const Faction faction = Faction.Exile;

                Proposal.SetupGet(value => value.MatchType).Returns(matchType);
                Proposal.SetupGet(value => value.Faction).Returns(faction);
                Proposal
                    .Setup(value => value.GetMatchingMaps())
                    .Returns(Array.Empty<IMatchingMap>());

                foreach ((Mock<IMatchingQueueProposalMember> member, Mock<IPlayer> player) in members.Zip(players))
                {
                    player.SetupGet(value => value.Faction1).Returns(faction);

                    var matchCharacter = new Mock<IMatchCharacter>(MockBehavior.Strict);
                    matchCharacter.SetupGet(value => value.Match).Returns((IMatch)null);
                    MatchManager
                        .Setup(value => value.GetMatchCharacter(member.Object.Identity))
                        .Returns(matchCharacter.Object);

                    var matchingCharacter = new Mock<IMatchingCharacter>(MockBehavior.Strict);
                    matchingCharacter
                        .Setup(value => value.GetMatchingCharacterQueue(matchType))
                        .Returns((IMatchingCharacterQueue)null);
                    matchingCharacter
                        .Setup(value => value.GetMatchingCharacterQueues())
                        .Returns(Array.Empty<IMatchingCharacterQueue>());
                    MatchingManager
                        .Setup(value => value.GetMatchingCharacter(member.Object.Identity))
                        .Returns(matchingCharacter.Object);
                }

                MatchingDataManager
                    .Setup(value => value.IsCompositionEnforced(matchType))
                    .Returns(true);
                var roleResult = new Mock<IMatchingRoleEnforcerResult>(MockBehavior.Strict);
                roleResult.SetupGet(value => value.Success).Returns(true);
                MatchingRoleEnforcer
                    .Setup(value => value.Check(It.IsAny<IEnumerable<IMatchingQueueProposalMember>>()))
                    .Returns(roleResult.Object);
            }

            private void AddMember(Identity identity, Role submittedRoles)
            {
                var member = new Mock<IMatchingQueueProposalMember>(MockBehavior.Strict);
                member.SetupGet(value => value.Identity).Returns(identity);
                member.SetupGet(value => value.Roles).Returns(submittedRoles);
                members.Add(member);
            }

            private Identity CreateIdentity()
            {
                return new Identity
                {
                    RealmId = 1,
                    Id = nextCharacterId++
                };
            }
        }

        private sealed class RoleCheckFixture
        {
            public Identity Identity { get; } = new()
            {
                RealmId = 1,
                Id = 1ul
            };

            public Mock<IMatchingDataManager> MatchingDataManager { get; } = new(MockBehavior.Strict);
            public Mock<IMatchingRoleCheckMember> Member { get; } = new(MockBehavior.Strict);
            public Mock<IPlayerManager> PlayerManager { get; } = new(MockBehavior.Strict);
            public MatchingRoleCheck RoleCheck { get; }
            public Role? StoredRoles { get; private set; }

            public RoleCheckFixture(Role? initialRoles = null)
            {
                StoredRoles = initialRoles;
                Member.SetupGet(value => value.Identity).Returns(Identity);
                Member.SetupGet(value => value.Roles).Returns(() => StoredRoles);
                Member.Setup(value => value.Initialise(Identity));
                Member
                    .Setup(value => value.SetRoles(It.IsAny<Role>()))
                    .Callback<Role>(roles => StoredRoles = roles);
                Member.Setup(value => value.Send(It.IsAny<IWritable>()));

                var memberFactory = new Mock<IFactory<IMatchingRoleCheckMember>>(MockBehavior.Strict);
                memberFactory.Setup(value => value.Resolve()).Returns(Member.Object);

                RoleCheck = new MatchingRoleCheck(
                    NullLogger<MatchingRoleCheck>.Instance,
                    PlayerManager.Object,
                    MatchingDataManager.Object,
                    memberFactory.Object);
                RoleCheck.Initialise(
                    new Mock<IMatchingQueueProposal>(MockBehavior.Strict).Object,
                    [Identity]);

                Member.Invocations.Clear();
            }

            public void SetupOnlinePlayer(Class playerClass, Role eligibleRoles)
            {
                var player = new Mock<IPlayer>(MockBehavior.Strict);
                player.SetupGet(value => value.Class).Returns(playerClass);
                PlayerManager
                    .Setup(value => value.GetPlayer(Identity))
                    .Returns(player.Object);
                MatchingDataManager
                    .Setup(value => value.GetDefaultRole(playerClass))
                    .Returns(eligibleRoles);
            }

            public void VerifyRoleDeclineBroadcast()
            {
                Member.Verify(
                    value => value.Send(It.Is<IWritable>(message =>
                        message.GetType() == typeof(ServerMatchingQueueResultAnnounce)
                        && ((ServerMatchingQueueResultAnnounce)message).Result == MatchingQueueResult.Role)),
                    Times.Once);
            }
        }
    }
}
