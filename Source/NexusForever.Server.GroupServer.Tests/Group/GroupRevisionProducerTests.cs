using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexusForever.API.Character.Client;
using NexusForever.Database.Group;
using NexusForever.Database.Group.Model;
using NexusForever.Database.Group.Repository;
using NexusForever.Game.Static.Group;
using NexusForever.Network.Internal.Message.Group;
using NexusForever.Network.Internal.Message.Player;
using NexusForever.Server.GroupServer.Character;
using NexusForever.Server.GroupServer.Group;
using NexusForever.Server.GroupServer.Network.Internal;
using CharacterEntity = NexusForever.Server.GroupServer.Character.Character;
using GroupEntity = NexusForever.Server.GroupServer.Group.Group;
using GroupIdentity = NexusForever.Server.GroupServer.Identity;

namespace NexusForever.Server.GroupServer.Tests.Group
{
    public class GroupRevisionProducerTests
    {
        private const ulong GroupId = 42ul;
        private const ulong InitialRevision = 7ul;
        private const ushort RealmId = 1;
        private const ulong LeaderId = 101ul;
        private const ulong MemberId = 202ul;
        private const ulong CandidateId = 303ul;

        private const GroupFlags SupportedGroupFlags = GroupFlags.OpenWorld
            | GroupFlags.Raid
            | GroupFlags.JoinRequestOpen
            | GroupFlags.JoinRequestClosed
            | GroupFlags.ReferralsOpen
            | GroupFlags.ReferralsClosed;

        private const GroupMemberInfoFlags AllDefinedMemberFlags = GroupMemberInfoFlags.CanInvite
            | GroupMemberInfoFlags.CanKick
            | GroupMemberInfoFlags.Disconnected
            | GroupMemberInfoFlags.Pending
            | GroupMemberInfoFlags.RoleFlags
            | GroupMemberInfoFlags.MainTank
            | GroupMemberInfoFlags.MainAssist
            | GroupMemberInfoFlags.RaidAssistant
            | GroupMemberInfoFlags.Ready
            | GroupMemberInfoFlags.RoleLocked
            | GroupMemberInfoFlags.CanMark
            | GroupMemberInfoFlags.HasSetReady;

        public static TheoryData<GroupMemberInfoFlags> ValidMemberChangedFlags => new()
        {
            GroupMemberInfoFlags.None,
            GroupMemberInfoFlags.CanInvite,
            GroupMemberInfoFlags.CanKick,
            GroupMemberInfoFlags.Tank,
            GroupMemberInfoFlags.Healer,
            GroupMemberInfoFlags.DPS,
            GroupMemberInfoFlags.MainTank,
            GroupMemberInfoFlags.MainAssist,
            GroupMemberInfoFlags.RaidAssistant,
            GroupMemberInfoFlags.Ready,
            GroupMemberInfoFlags.RoleLocked,
            GroupMemberInfoFlags.CanMark,
            GroupMemberInfoFlags.HasSetReady,
            GroupMemberInfoFlags.Ready | GroupMemberInfoFlags.HasSetReady,
        };

        public static TheoryData<GroupMemberInfoFlags, GroupMemberInfoFlags> InvalidMemberFlagChanges => new()
        {
            { (GroupMemberInfoFlags)1u, GroupMemberInfoFlags.None },
            { (GroupMemberInfoFlags)(1u << 15), GroupMemberInfoFlags.None },
            { AllDefinedMemberFlags, GroupMemberInfoFlags.Disconnected },
            { AllDefinedMemberFlags, GroupMemberInfoFlags.Pending },
            { AllDefinedMemberFlags, GroupMemberInfoFlags.Tank | GroupMemberInfoFlags.Healer },
            { AllDefinedMemberFlags, GroupMemberInfoFlags.CanInvite | GroupMemberInfoFlags.CanKick },
            { GroupMemberInfoFlags.Ready, GroupMemberInfoFlags.Ready | GroupMemberInfoFlags.HasSetReady },
            { GroupMemberInfoFlags.None, GroupMemberInfoFlags.HasSetReady },
            { GroupMemberInfoFlags.None, GroupMemberInfoFlags.Ready | GroupMemberInfoFlags.HasSetReady },
        };

        public static IEnumerable<object[]> StructurallyInvalidGroupFlags
        {
            get
            {
                for (var bit = 0; bit < 32; bit++)
                {
                    var flag = (GroupFlags)(1u << bit);
                    if ((flag & SupportedGroupFlags) == 0)
                        yield return [flag];
                }

                yield return [GroupFlags.JoinRequestOpen | GroupFlags.JoinRequestClosed];
                yield return [GroupFlags.ReferralsOpen | GroupFlags.ReferralsClosed];
                yield return [SupportedGroupFlags];
                yield return [(GroupFlags)uint.MaxValue];
            }
        }

        public static TheoryData<GroupFlags, GroupFlags> InvalidGroupFlagTransitions => new()
        {
            { GroupFlags.None, GroupFlags.OpenWorld },
            { GroupFlags.OpenWorld, GroupFlags.None },
            { GroupFlags.None, GroupFlags.Raid },
            { GroupFlags.Raid, GroupFlags.None },
            { GroupFlags.OpenWorld | GroupFlags.Raid, GroupFlags.OpenWorld },
        };

        public static TheoryData<LootRule, LootRule, LootThreshold, HarvestLootRule> InvalidLootRuleTuples => new()
        {
            { (LootRule)4, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { (LootRule)5, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { (LootRule)6, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { (LootRule)7, LootRule.NeedBeforeGreed, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, (LootRule)4, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, (LootRule)5, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, (LootRule)6, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, (LootRule)7, LootThreshold.Good, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)0, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)8, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)9, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)10, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)11, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)12, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)13, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)14, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, (LootThreshold)15, HarvestLootRule.FirstTagger },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, LootThreshold.Good, (HarvestLootRule)2 },
            { LootRule.NeedBeforeGreed, LootRule.NeedBeforeGreed, LootThreshold.Good, (HarvestLootRule)3 },
        };

        [Fact]
        public async Task AddMember_AdvancesOnceAndPublishesTheAdvancedRevision()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            GroupMember addedMember = await group.AddMemberAsync(Identity(CandidateId));

            Assert.NotNull(addedMember);
            Assert.Equal(InitialRevision + 1ul, group.Revision);
            Assert.Equal(3, group.GetMembers().Count());
            Assert.Equal(InitialRevision + 1ul, fixture.SingleMessage<GroupMemberJoinedMessage>().Group.Revision);
            Assert.Equal(InitialRevision + 1ul, fixture.SingleMessage<PlayerGroupAssociationUpdatedMessage>().Group.Revision);
            Assert.Equal(InitialRevision + 1ul, fixture.SingleMessage<GroupMemberAddedMessage>().Group.Revision);
            Assert.Equal(3, fixture.PendingMessages.Count);
        }

        [Fact]
        public async Task DuplicateAddAndMissingRemove_DoNotAdvanceOrPublish()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            CharacterEntity leader = await fixture.CharacterManager.GetCharacterAsync(Identity(LeaderId));

            GroupMember duplicate = await group.AddMemberAsync(leader);
            GroupActionResult removeResult = await group.RemoveMemberAsync(Identity(CandidateId), RemoveReason.Left);

            Assert.Null(duplicate);
            Assert.Equal(GroupActionResult.InvalidGroup, removeResult);
            Assert.Equal(InitialRevision, group.Revision);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task RemoveMember_AdvancesOnceAndPublishesThePreRemovalSnapshotRevision()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            GroupActionResult result = await group.RemoveMemberAsync(Identity(MemberId), RemoveReason.Left);

            Assert.Equal(GroupActionResult.LeaveSuccess, result);
            Assert.Equal(InitialRevision + 1ul, group.Revision);
            Assert.Null(group.GetMember(Identity(MemberId)));
            Assert.Equal(InitialRevision + 1ul, fixture.SingleMessage<GroupMemberLeftMessage>().Group.Revision);
            Assert.Equal(InitialRevision + 1ul, fixture.SingleMessage<GroupMemberRemovedMessage>().Group.Revision);
            Assert.Null(fixture.SingleMessage<PlayerGroupAssociationUpdatedMessage>().Group);
            Assert.Equal(3, fixture.PendingMessages.Count);
        }

        [Fact]
        public async Task Disband_AdvancesOnceAndPublishesTheTombstoneRevision()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            GroupActionResult result = await group.DisbandAsync(Identity(LeaderId));

            Assert.Equal(GroupActionResult.DisbandSuccess, result);
            Assert.Equal(InitialRevision + 1ul, group.Revision);
            GroupDisbandedMessage tombstone = fixture.SingleMessage<GroupDisbandedMessage>();
            Assert.Equal(GroupId, tombstone.Group.Id);
            Assert.Equal(InitialRevision + 1ul, tombstone.Group.Revision);
            GroupMemberLeftMessage[] leftMessages = fixture.Messages<GroupMemberLeftMessage>();
            Assert.Equal(2, leftMessages.Length);
            Assert.All(leftMessages, message =>
                Assert.Equal(InitialRevision + 1ul, message.Group.Revision));
            Assert.Equal(5, fixture.PendingMessages.Count);
        }

        [Fact]
        public async Task PromoteMember_PublishesTheExactMultiMutationRevisionSequence()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            GroupActionResult result = await group.PromoteMemberAsync(Identity(LeaderId), Identity(MemberId));

            Assert.Equal(GroupActionResult.PromoteSuccess, result);
            Assert.Equal(InitialRevision + 3ul, group.Revision);
            Assert.Equal(Identity(MemberId), group.Leader);
            Assert.Equal(GroupMemberInfoFlags.None, group.GetMember(Identity(LeaderId)).Flags);
            Assert.Equal(GroupMemberInfoFlags.GroupAdminFlags, group.GetMember(Identity(MemberId)).Flags);

            GroupMemberFlagsUpdatedMessage oldLeaderUpdate = fixture.Messages<GroupMemberFlagsUpdatedMessage>()
                .Single(message => message.Member.Identity.Id == LeaderId);
            Assert.Equal(InitialRevision + 1ul, oldLeaderUpdate.Group.Revision);
            Assert.Equal(LeaderId, oldLeaderUpdate.Group.Leader.Id);
            Assert.Equal(GroupMemberInfoFlags.None, oldLeaderUpdate.Member.Flags);
            Assert.True(oldLeaderUpdate.FromPromotion);

            GroupMemberFlagsUpdatedMessage newLeaderUpdate = fixture.Messages<GroupMemberFlagsUpdatedMessage>()
                .Single(message => message.Member.Identity.Id == MemberId);
            Assert.Equal(InitialRevision + 3ul, newLeaderUpdate.Group.Revision);
            Assert.Equal(MemberId, newLeaderUpdate.Group.Leader.Id);
            Assert.Equal(GroupMemberInfoFlags.GroupAdminFlags, newLeaderUpdate.Member.Flags);
            Assert.True(newLeaderUpdate.FromPromotion);

            GroupMemberPromotedMessage promoted = fixture.SingleMessage<GroupMemberPromotedMessage>();
            Assert.Equal(InitialRevision + 3ul, promoted.Group.Revision);
            Assert.Equal(MemberId, promoted.Group.Leader.Id);
            Assert.Equal(MemberId, promoted.Member.Identity.Id);
            Assert.Equal(3, fixture.PendingMessages.Count);
        }

        [Fact]
        public async Task GroupFlags_ChangedAndUnchangedValuesPublishAuthoritativeRevisions()
        {
            using var changedFixture = new ProducerFixture();
            GroupEntity changedGroup = changedFixture.CreateGroup(flags: GroupFlags.OpenWorld);
            GroupFlags changedFlags = GroupFlags.OpenWorld | GroupFlags.Raid | GroupFlags.JoinRequestOpen;

            GroupActionResult changedResult = await changedGroup.SetGroupFlagsAsync(Identity(LeaderId), changedFlags);

            Assert.Equal(GroupActionResult.FlagsSuccess, changedResult);
            Assert.Equal(InitialRevision + 1ul, changedGroup.Revision);
            Assert.Equal(changedFlags, changedGroup.Flags);
            Assert.Equal(InitialRevision + 1ul, changedFixture.SingleMessage<GroupFlagsUpdatedMessage>().Group.Revision);
            Assert.Equal(InitialRevision + 1ul, changedFixture.SingleMessage<GroupMaxSizeUpdatedMessage>().Group.Revision);
            Assert.Equal(2, changedFixture.PendingMessages.Count);

            using var unchangedFixture = new ProducerFixture();
            GroupEntity unchangedGroup = unchangedFixture.CreateGroup(flags: changedFlags);

            GroupActionResult unchangedResult = await unchangedGroup.SetGroupFlagsAsync(Identity(LeaderId), changedFlags);

            Assert.Equal(GroupActionResult.FlagsSuccess, unchangedResult);
            Assert.Equal(InitialRevision, unchangedGroup.Revision);
            Assert.Equal(InitialRevision, unchangedFixture.SingleMessage<GroupFlagsUpdatedMessage>().Group.Revision);
            Assert.Empty(unchangedFixture.Messages<GroupMaxSizeUpdatedMessage>());
            Assert.Single(unchangedFixture.PendingMessages);
        }

        [Theory]
        [MemberData(nameof(StructurallyInvalidGroupFlags))]
        public async Task GroupFlags_StructurallyInvalidLeaderValueDoesNotMutateRevisionOrPublish(GroupFlags flags)
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            GroupActionResult result = await group.SetGroupFlagsAsync(Identity(LeaderId), flags);

            Assert.Equal(GroupActionResult.FlagsFailed, result);
            Assert.Equal(InitialRevision, group.Revision);
            Assert.Equal(GroupFlags.None, group.Flags);
            Assert.Empty(fixture.PendingMessages);
        }

        [Theory]
        [MemberData(nameof(InvalidGroupFlagTransitions))]
        public async Task GroupFlags_ImmutableOrAddOnlyTransitionDoesNotMutateRevisionOrPublish(
            GroupFlags currentFlags,
            GroupFlags requestedFlags)
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup(flags: currentFlags);

            GroupActionResult result = await group.SetGroupFlagsAsync(Identity(LeaderId), requestedFlags);

            Assert.Equal(GroupActionResult.FlagsFailed, result);
            Assert.Equal(InitialRevision, group.Revision);
            Assert.Equal(currentFlags, group.Flags);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task GroupFlags_OpenWorldRaidAdditionPreservesOriginAndPublishesMaxSize()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup(flags: GroupFlags.OpenWorld);
            GroupFlags requestedFlags = GroupFlags.OpenWorld
                | GroupFlags.Raid
                | GroupFlags.JoinRequestClosed
                | GroupFlags.ReferralsOpen;

            GroupActionResult result = await group.SetGroupFlagsAsync(Identity(LeaderId), requestedFlags);

            Assert.Equal(GroupActionResult.FlagsSuccess, result);
            Assert.Equal(InitialRevision + 1ul, group.Revision);
            Assert.Equal(requestedFlags, group.Flags);
            Assert.Equal(requestedFlags, fixture.SingleMessage<GroupFlagsUpdatedMessage>().Group.Flags);
            Assert.Equal(20u, fixture.SingleMessage<GroupMaxSizeUpdatedMessage>().Group.MaxGroupSize);
            Assert.Equal(2, fixture.PendingMessages.Count);
        }

        [Fact]
        public async Task GroupFlags_TriStateChangesPreserveRaidWithoutRepublishingMaxSize()
        {
            using var fixture = new ProducerFixture();
            GroupFlags currentFlags = GroupFlags.OpenWorld
                | GroupFlags.Raid
                | GroupFlags.JoinRequestOpen
                | GroupFlags.ReferralsClosed;
            GroupEntity group = fixture.CreateGroup(flags: currentFlags);
            GroupFlags requestedFlags = GroupFlags.OpenWorld
                | GroupFlags.Raid
                | GroupFlags.JoinRequestClosed
                | GroupFlags.ReferralsOpen;

            GroupActionResult result = await group.SetGroupFlagsAsync(Identity(LeaderId), requestedFlags);

            Assert.Equal(GroupActionResult.FlagsSuccess, result);
            Assert.Equal(InitialRevision + 1ul, group.Revision);
            Assert.Equal(requestedFlags, group.Flags);
            Assert.Equal(requestedFlags, fixture.SingleMessage<GroupFlagsUpdatedMessage>().Group.Flags);
            Assert.Empty(fixture.Messages<GroupMaxSizeUpdatedMessage>());
            Assert.Single(fixture.PendingMessages);
        }

        [Fact]
        public async Task LootRules_ChangedAndUnchangedTuplesPublishAuthoritativeRevisions()
        {
            using var changedFixture = new ProducerFixture();
            GroupEntity changedGroup = changedFixture.CreateGroup();

            GroupActionResult changedResult = await changedGroup.SetLootRulesAsync(
                Identity(LeaderId),
                LootRule.RoundRobin,
                LootRule.Master,
                LootThreshold.Excellent,
                HarvestLootRule.RoundRobin);

            Assert.Equal(GroupActionResult.ChangeSettingsSuccess, changedResult);
            Assert.Equal(InitialRevision + 1ul, changedGroup.Revision);
            GroupLootRulesUpdatedMessage changed = changedFixture.SingleMessage<GroupLootRulesUpdatedMessage>();
            Assert.Equal(InitialRevision + 1ul, changed.Group.Revision);
            Assert.Equal(LootRule.RoundRobin, changed.Group.NormalRule);
            Assert.Equal(LootRule.Master, changed.Group.ThresholdRule);
            Assert.Equal(LootThreshold.Excellent, changed.Group.ThresholdQuality);
            Assert.Equal(HarvestLootRule.RoundRobin, changed.Group.HarvestRule);

            using var unchangedFixture = new ProducerFixture();
            GroupEntity unchangedGroup = unchangedFixture.CreateGroup();

            GroupActionResult unchangedResult = await unchangedGroup.SetLootRulesAsync(
                Identity(LeaderId),
                LootRule.NeedBeforeGreed,
                LootRule.NeedBeforeGreed,
                LootThreshold.Good,
                HarvestLootRule.FirstTagger);

            Assert.Equal(GroupActionResult.ChangeSettingsSuccess, unchangedResult);
            Assert.Equal(InitialRevision, unchangedGroup.Revision);
            Assert.Equal(InitialRevision, unchangedFixture.SingleMessage<GroupLootRulesUpdatedMessage>().Group.Revision);
            Assert.Single(unchangedFixture.PendingMessages);
        }

        [Theory]
        [MemberData(nameof(InvalidLootRuleTuples))]
        public async Task LootRules_InvalidLeaderTupleDoesNotMutateRevisionOrPublish(
            LootRule normalRule,
            LootRule thresholdRule,
            LootThreshold thresholdQuality,
            HarvestLootRule harvestRule)
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            GroupActionResult result = await group.SetLootRulesAsync(
                Identity(LeaderId),
                normalRule,
                thresholdRule,
                thresholdQuality,
                harvestRule);

            Assert.Equal(GroupActionResult.ChangeSettingsFailed, result);
            Assert.Equal(InitialRevision, group.Revision);
            AssertLootRules(
                group,
                LootRule.NeedBeforeGreed,
                LootRule.NeedBeforeGreed,
                LootThreshold.Good,
                HarvestLootRule.FirstTagger);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task LootRules_InvalidTuplePreservesMemberAndLeaderResultOrdering()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            GroupActionResult missingMemberResult = await group.SetLootRulesAsync(
                Identity(CandidateId),
                (LootRule)byte.MaxValue,
                (LootRule)byte.MaxValue,
                (LootThreshold)byte.MaxValue,
                (HarvestLootRule)byte.MaxValue);
            GroupActionResult nonLeaderResult = await group.SetLootRulesAsync(
                Identity(MemberId),
                (LootRule)byte.MaxValue,
                (LootRule)byte.MaxValue,
                (LootThreshold)byte.MaxValue,
                (HarvestLootRule)byte.MaxValue);

            Assert.Equal(GroupActionResult.InvalidGroup, missingMemberResult);
            Assert.Equal(GroupActionResult.ChangeSettingsFailed, nonLeaderResult);
            Assert.Equal(InitialRevision, group.Revision);
            AssertLootRules(
                group,
                LootRule.NeedBeforeGreed,
                LootRule.NeedBeforeGreed,
                LootThreshold.Good,
                HarvestLootRule.FirstTagger);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task LootRules_ValidLeaderTupleCanReplaceExistingInvalidState()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            group.LootRule = (LootRule)byte.MaxValue;
            group.LootRuleThreshold = (LootRule)byte.MaxValue;
            group.LootThreshold = (LootThreshold)byte.MaxValue;
            group.LootRuleHarvest = (HarvestLootRule)byte.MaxValue;

            GroupActionResult result = await group.SetLootRulesAsync(
                Identity(LeaderId),
                LootRule.RoundRobin,
                LootRule.Master,
                LootThreshold.Excellent,
                HarvestLootRule.RoundRobin);

            Assert.Equal(GroupActionResult.ChangeSettingsSuccess, result);
            Assert.Equal(InitialRevision + 1ul, group.Revision);
            AssertLootRules(
                group,
                LootRule.RoundRobin,
                LootRule.Master,
                LootThreshold.Excellent,
                HarvestLootRule.RoundRobin);
            GroupLootRulesUpdatedMessage message = fixture.SingleMessage<GroupLootRulesUpdatedMessage>();
            Assert.Equal(InitialRevision + 1ul, message.Group.Revision);
            Assert.Equal(LootRule.RoundRobin, message.Group.NormalRule);
            Assert.Equal(LootRule.Master, message.Group.ThresholdRule);
            Assert.Equal(LootThreshold.Excellent, message.Group.ThresholdQuality);
            Assert.Equal(HarvestLootRule.RoundRobin, message.Group.HarvestRule);
            Assert.Single(fixture.PendingMessages);
        }

        [Fact]
        public async Task MemberFlags_ChangedAndUnchangedValuesPublishAuthoritativeRevisions()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            GroupMember member = group.GetMember(Identity(MemberId));

            GroupActionResult setResult = await group.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(MemberId),
                GroupMemberInfoFlags.GroupMemberFlags | GroupMemberInfoFlags.Tank,
                GroupMemberInfoFlags.Tank);
            await member.SetFlagAsync(GroupMemberInfoFlags.Tank);
            GroupActionResult removeResult = await group.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(MemberId),
                GroupMemberInfoFlags.GroupMemberFlags,
                GroupMemberInfoFlags.Tank);
            await member.RemoveFlagAsync(GroupMemberInfoFlags.Tank);

            Assert.Equal(GroupActionResult.MemberFlagsSuccess, setResult);
            Assert.Equal(GroupActionResult.MemberFlagsSuccess, removeResult);
            Assert.Equal(InitialRevision + 2ul, group.Revision);
            Assert.Equal(GroupMemberInfoFlags.GroupMemberFlags, member.Flags);
            GroupMemberFlagsUpdatedMessage[] messages = fixture.Messages<GroupMemberFlagsUpdatedMessage>();
            Assert.Equal(4, messages.Length);
            Assert.Equal(2, messages.Count(message => message.Group.Revision == InitialRevision + 1ul));
            Assert.Equal(2, messages.Count(message => message.Group.Revision == InitialRevision + 2ul));
            Assert.All(messages, message => Assert.Equal(MemberId, message.Member.Identity.Id));
            Assert.All(messages.Where(message => message.Group.Revision == InitialRevision + 1ul), message =>
                Assert.True(message.Member.Flags.HasFlag(GroupMemberInfoFlags.Tank)));
            Assert.All(messages.Where(message => message.Group.Revision == InitialRevision + 2ul), message =>
                Assert.Equal(GroupMemberInfoFlags.GroupMemberFlags, message.Member.Flags));
        }

        [Theory]
        [MemberData(nameof(ValidMemberChangedFlags))]
        public async Task MemberFlags_ExactStockDeltaUsesDirectionWithoutReplacingAuthoritativeSnapshot(
            GroupMemberInfoFlags changedFlag)
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            GroupMember target = group.GetMember(Identity(MemberId));

            GroupActionResult result = await group.SetMemberFlagsAsync(
                Identity(LeaderId),
                Identity(MemberId),
                AllDefinedMemberFlags,
                changedFlag);

            Assert.Equal(GroupActionResult.MemberFlagsSuccess, result);
            ulong expectedRevision = changedFlag is GroupMemberInfoFlags.None or GroupMemberInfoFlags.CanMark
                ? InitialRevision
                : InitialRevision + 1ul;
            Assert.Equal(expectedRevision, group.Revision);
            Assert.Equal(changedFlag, target.Flags & changedFlag);
            Assert.False(target.Flags.HasFlag(GroupMemberInfoFlags.Disconnected));
            Assert.False(target.Flags.HasFlag(GroupMemberInfoFlags.Pending));
            GroupMemberFlagsUpdatedMessage message = fixture.SingleMessage<GroupMemberFlagsUpdatedMessage>();
            Assert.Equal(expectedRevision, message.Group.Revision);
            Assert.Equal(target.Flags, message.Member.Flags);
        }

        [Theory]
        [MemberData(nameof(InvalidMemberFlagChanges))]
        public async Task MemberFlags_InvalidLeaderChangeDoesNotMutateRevisionOrPublish(
            GroupMemberInfoFlags currentFlags,
            GroupMemberInfoFlags changedFlag)
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            GroupMember target = group.GetMember(Identity(MemberId));
            GroupMemberInfoFlags originalFlags = target.Flags;

            GroupActionResult result = await group.SetMemberFlagsAsync(
                Identity(LeaderId),
                Identity(MemberId),
                currentFlags,
                changedFlag);

            Assert.Equal(GroupActionResult.MemberFlagsFailed, result);
            Assert.Equal(InitialRevision, group.Revision);
            Assert.Equal(originalFlags, target.Flags);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task MemberFlags_MissingActorOrTargetReturnsInvalidGroupBeforeFlagValidation()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            GroupActionResult missingActor = await group.SetMemberFlagsAsync(
                Identity(CandidateId),
                Identity(MemberId),
                (GroupMemberInfoFlags)uint.MaxValue,
                (GroupMemberInfoFlags)uint.MaxValue);
            GroupActionResult missingTarget = await group.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(CandidateId),
                (GroupMemberInfoFlags)uint.MaxValue,
                (GroupMemberInfoFlags)uint.MaxValue);

            Assert.Equal(GroupActionResult.InvalidGroup, missingActor);
            Assert.Equal(GroupActionResult.InvalidGroup, missingTarget);
            Assert.Equal(InitialRevision, group.Revision);
            Assert.Equal(GroupMemberInfoFlags.GroupMemberFlags, group.GetMember(Identity(MemberId)).Flags);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task MemberFlags_RoleAddPreservesUnchangedRolesAndServerOwnedFlags()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            GroupMember member = group.GetMember(Identity(MemberId));
            member.Flags |= GroupMemberInfoFlags.Disconnected
                | GroupMemberInfoFlags.Pending
                | GroupMemberInfoFlags.Healer
                | GroupMemberInfoFlags.DPS;

            GroupActionResult result = await group.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(MemberId),
                GroupMemberInfoFlags.GroupMemberFlags
                    | GroupMemberInfoFlags.Disconnected
                    | GroupMemberInfoFlags.Pending
                    | GroupMemberInfoFlags.Tank,
                GroupMemberInfoFlags.Tank);

            Assert.Equal(GroupActionResult.MemberFlagsSuccess, result);
            Assert.Equal(
                GroupMemberInfoFlags.GroupMemberFlags
                    | GroupMemberInfoFlags.Disconnected
                    | GroupMemberInfoFlags.Pending
                    | GroupMemberInfoFlags.Tank
                    | GroupMemberInfoFlags.Healer
                    | GroupMemberInfoFlags.DPS,
                member.Flags);
            Assert.Equal(InitialRevision + 1ul, group.Revision);
            Assert.Equal(member.Flags, fixture.SingleMessage<GroupMemberFlagsUpdatedMessage>().Member.Flags);
        }

        [Theory]
        [InlineData(GroupMemberInfoFlags.HasSetReady, false)]
        [InlineData(GroupMemberInfoFlags.Ready | GroupMemberInfoFlags.HasSetReady, true)]
        public async Task MemberFlags_FirstReadyResponseAddsOnlyDesiredReadyState(
            GroupMemberInfoFlags changedFlag,
            bool ready)
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            GroupMember member = group.GetMember(Identity(MemberId));
            member.Flags |= GroupMemberInfoFlags.Pending;
            GroupMemberInfoFlags currentFlags = member.Flags | changedFlag;

            GroupActionResult result = await group.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(MemberId),
                currentFlags,
                changedFlag);

            Assert.Equal(GroupActionResult.MemberFlagsSuccess, result);
            Assert.True(member.Flags.HasFlag(GroupMemberInfoFlags.Pending));
            Assert.True(member.Flags.HasFlag(GroupMemberInfoFlags.HasSetReady));
            Assert.Equal(ready, member.Flags.HasFlag(GroupMemberInfoFlags.Ready));
            Assert.Equal(InitialRevision + 1ul, group.Revision);
            Assert.Equal(member.Flags, fixture.SingleMessage<GroupMemberFlagsUpdatedMessage>().Member.Flags);
        }

        [Fact]
        public async Task MemberFlags_AuthorisationUsesActorRoleLockAndRaidAssistantWhileLeaderBypasses()
        {
            using var targetAssistantFixture = new ProducerFixture();
            GroupEntity targetAssistantGroup = targetAssistantFixture.CreateGroup();
            GroupMember ordinaryActor = targetAssistantGroup.GetMember(Identity(MemberId));
            GroupMember assistantTarget = targetAssistantGroup.GetMember(Identity(LeaderId));
            assistantTarget.Flags |= GroupMemberInfoFlags.RaidAssistant;

            GroupActionResult targetAssistantResult = await targetAssistantGroup.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(LeaderId),
                assistantTarget.Flags | GroupMemberInfoFlags.Tank,
                GroupMemberInfoFlags.Tank);

            Assert.Equal(GroupActionResult.MemberFlagsFailed, targetAssistantResult);
            Assert.False(assistantTarget.Flags.HasFlag(GroupMemberInfoFlags.Tank));
            Assert.Equal(InitialRevision, targetAssistantGroup.Revision);
            Assert.Empty(targetAssistantFixture.PendingMessages);

            using var lockedFixture = new ProducerFixture();
            GroupEntity lockedGroup = lockedFixture.CreateGroup();
            GroupMember lockedActor = lockedGroup.GetMember(Identity(MemberId));
            lockedActor.Flags |= GroupMemberInfoFlags.RoleLocked;

            GroupActionResult lockedResult = await lockedGroup.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(MemberId),
                lockedActor.Flags | GroupMemberInfoFlags.Tank,
                GroupMemberInfoFlags.Tank);

            Assert.Equal(GroupActionResult.MemberFlagsFailed, lockedResult);
            Assert.False(lockedActor.Flags.HasFlag(GroupMemberInfoFlags.Tank));
            Assert.Equal(InitialRevision, lockedGroup.Revision);
            Assert.Empty(lockedFixture.PendingMessages);

            using var assistantFixture = new ProducerFixture();
            GroupEntity assistantGroup = assistantFixture.CreateGroup();
            GroupMember assistant = assistantGroup.GetMember(Identity(MemberId));
            GroupMember leader = assistantGroup.GetMember(Identity(LeaderId));
            assistant.Flags |= GroupMemberInfoFlags.RaidAssistant;
            leader.Flags |= GroupMemberInfoFlags.RoleLocked;

            GroupActionResult assistantResult = await assistantGroup.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(LeaderId),
                leader.Flags | GroupMemberInfoFlags.Tank,
                GroupMemberInfoFlags.Tank);

            Assert.Equal(GroupActionResult.MemberFlagsSuccess, assistantResult);
            Assert.True(leader.Flags.HasFlag(GroupMemberInfoFlags.Tank));
            Assert.Equal(InitialRevision + 1ul, assistantGroup.Revision);
            Assert.Single(assistantFixture.PendingMessages);

            using var leaderFixture = new ProducerFixture();
            GroupEntity leaderGroup = leaderFixture.CreateGroup();
            GroupMember leaderActor = leaderGroup.GetMember(Identity(LeaderId));
            GroupMember leaderTarget = leaderGroup.GetMember(Identity(MemberId));
            leaderActor.Flags |= GroupMemberInfoFlags.RoleLocked;

            GroupActionResult leaderResult = await leaderGroup.SetMemberFlagsAsync(
                Identity(LeaderId),
                Identity(MemberId),
                leaderTarget.Flags | GroupMemberInfoFlags.CanKick,
                GroupMemberInfoFlags.CanKick);

            Assert.Equal(GroupActionResult.MemberFlagsSuccess, leaderResult);
            Assert.True(leaderTarget.Flags.HasFlag(GroupMemberInfoFlags.CanKick));
            Assert.Equal(InitialRevision + 1ul, leaderGroup.Revision);
            Assert.Single(leaderFixture.PendingMessages);
        }

        [Fact]
        public async Task MemberFlags_ValidReplayPublishesAuthoritativeStateWithoutRevisionAdvance()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            GroupMember member = group.GetMember(Identity(MemberId));
            member.Flags |= GroupMemberInfoFlags.Tank;

            GroupActionResult result = await group.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(MemberId),
                member.Flags,
                GroupMemberInfoFlags.Tank);

            Assert.Equal(GroupActionResult.MemberFlagsSuccess, result);
            Assert.Equal(InitialRevision, group.Revision);
            Assert.Equal(member.Flags, fixture.SingleMessage<GroupMemberFlagsUpdatedMessage>().Member.Flags);
            Assert.Single(fixture.PendingMessages);
        }

        [Fact]
        public async Task ReadyCheck_PublishesEachMemberMutationAndTheFinalRevision()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            GroupActionResult? result = await group.StartReadyCheckAsync(Identity(LeaderId), "Ready?");

            Assert.Null(result);
            Assert.Equal(InitialRevision + 2ul, group.Revision);
            GroupMemberFlagsUpdatedMessage leaderUpdate = fixture.Messages<GroupMemberFlagsUpdatedMessage>()
                .Single(message => message.Member.Identity.Id == LeaderId);
            GroupMemberFlagsUpdatedMessage memberUpdate = fixture.Messages<GroupMemberFlagsUpdatedMessage>()
                .Single(message => message.Member.Identity.Id == MemberId);
            Assert.Equal(InitialRevision + 1ul, leaderUpdate.Group.Revision);
            Assert.Equal(InitialRevision + 2ul, memberUpdate.Group.Revision);
            Assert.True(leaderUpdate.Member.Flags.HasFlag(GroupMemberInfoFlags.Pending));
            Assert.True(memberUpdate.Member.Flags.HasFlag(GroupMemberInfoFlags.Pending));
            Assert.Equal(InitialRevision + 2ul, fixture.SingleMessage<GroupReadyCheckStartedMessage>().Group.Revision);
            Assert.Equal(3, fixture.PendingMessages.Count);
        }

        [Fact]
        public async Task ReadyCheck_AlreadyPendingMembersPublishTheSameAuthoritativeRevision()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();
            group.GetMember(Identity(LeaderId)).Flags |= GroupMemberInfoFlags.Pending;
            group.GetMember(Identity(MemberId)).Flags |= GroupMemberInfoFlags.Pending;

            GroupActionResult? result = await group.StartReadyCheckAsync(Identity(LeaderId), "Still ready?");

            Assert.Null(result);
            Assert.Equal(InitialRevision, group.Revision);
            Assert.Equal(2, fixture.Messages<GroupMemberFlagsUpdatedMessage>().Length);
            Assert.All(fixture.Messages<GroupMemberFlagsUpdatedMessage>(), message =>
                Assert.Equal(InitialRevision, message.Group.Revision));
            Assert.Equal(InitialRevision, fixture.SingleMessage<GroupReadyCheckStartedMessage>().Group.Revision);
            Assert.Equal(3, fixture.PendingMessages.Count);
        }

        [Fact]
        public async Task AuthorisationFailuresAndPromotionNoOp_DoNotAdvanceOrPublish()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup();

            Assert.Equal(GroupActionResult.DisbandFailed, await group.DisbandAsync(Identity(MemberId)));
            Assert.Equal(GroupActionResult.PromoteFailed, await group.PromoteMemberAsync(Identity(MemberId), Identity(LeaderId)));
            Assert.Equal(GroupActionResult.KickFailed, await group.KickMemberAsync(Identity(MemberId), Identity(LeaderId), RemoveReason.Kicked));
            Assert.Equal(GroupActionResult.FlagsFailed, await group.SetGroupFlagsAsync(Identity(MemberId), GroupFlags.Raid));
            Assert.Equal(GroupActionResult.ChangeSettingsFailed, await group.SetLootRulesAsync(
                Identity(MemberId), LootRule.RoundRobin, LootRule.Master, LootThreshold.Excellent, HarvestLootRule.RoundRobin));
            Assert.Equal(GroupActionResult.MemberFlagsFailed, await group.SetMemberFlagsAsync(
                Identity(MemberId),
                Identity(LeaderId),
                GroupMemberInfoFlags.GroupAdminFlags | GroupMemberInfoFlags.Tank,
                GroupMemberInfoFlags.Tank));
            Assert.Equal(GroupActionResult.PromoteSuccess, await group.PromoteMemberAsync(Identity(LeaderId), Identity(LeaderId)));

            Assert.Equal(InitialRevision, group.Revision);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task ChangedMutation_RejectsZeroRevisionBeforeStateOrOutboxMutation()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup(revision: 0ul);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                group.SetGroupFlagsAsync(Identity(LeaderId), GroupFlags.JoinRequestOpen));

            Assert.Equal(0ul, group.Revision);
            Assert.Equal(GroupFlags.None, group.Flags);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task ChangedMutation_CheckedOverflowLeavesStateAndOutboxUnchanged()
        {
            using var fixture = new ProducerFixture();
            GroupEntity group = fixture.CreateGroup(revision: ulong.MaxValue);

            await Assert.ThrowsAsync<OverflowException>(() =>
                group.SetGroupFlagsAsync(Identity(LeaderId), GroupFlags.JoinRequestOpen));

            Assert.Equal(ulong.MaxValue, group.Revision);
            Assert.Equal(GroupFlags.None, group.Flags);
            Assert.Empty(fixture.PendingMessages);
        }

        [Fact]
        public async Task UnchangedMutation_StillEnforcesZeroRevisionButDoesNotOverflowAtMax()
        {
            using var zeroFixture = new ProducerFixture();
            GroupEntity zeroGroup = zeroFixture.CreateGroup(revision: 0ul);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                zeroGroup.SetGroupFlagsAsync(Identity(LeaderId), GroupFlags.None));
            Assert.Empty(zeroFixture.PendingMessages);

            using var maxFixture = new ProducerFixture();
            GroupEntity maxGroup = maxFixture.CreateGroup(revision: ulong.MaxValue);

            GroupActionResult result = await maxGroup.SetGroupFlagsAsync(Identity(LeaderId), GroupFlags.None);

            Assert.Equal(GroupActionResult.FlagsSuccess, result);
            Assert.Equal(ulong.MaxValue, maxGroup.Revision);
            Assert.Equal(ulong.MaxValue, maxFixture.SingleMessage<GroupFlagsUpdatedMessage>().Group.Revision);
        }

        private static GroupIdentity Identity(ulong id)
        {
            return new GroupIdentity
            {
                Id      = id,
                RealmId = RealmId,
            };
        }

        private static void AssertLootRules(
            GroupEntity group,
            LootRule normalRule,
            LootRule thresholdRule,
            LootThreshold thresholdQuality,
            HarvestLootRule harvestRule)
        {
            Assert.Equal(normalRule, group.LootRule);
            Assert.Equal(thresholdRule, group.LootRuleThreshold);
            Assert.Equal(thresholdQuality, group.LootThreshold);
            Assert.Equal(harvestRule, group.LootRuleHarvest);
            Assert.Equal(normalRule, group.Model.LootRule);
            Assert.Equal(thresholdRule, group.Model.LootRuleThreshold);
            Assert.Equal(thresholdQuality, group.Model.LootThreshold);
            Assert.Equal(harvestRule, group.Model.LootRuleHarvest);
        }

        private sealed class ProducerFixture : IDisposable
        {
            private readonly GroupContext context;
            private readonly ServiceProvider provider;
            private readonly IServiceScope scope;
            private readonly HttpClient httpClient;

            public CharacterManager CharacterManager => scope.ServiceProvider.GetRequiredService<CharacterManager>();

            public IReadOnlyList<InternalMessageModel> PendingMessages => context.ChangeTracker
                .Entries<InternalMessageModel>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity)
                .ToArray();

            public ProducerFixture()
            {
                DbContextOptions<GroupContext> options = new DbContextOptionsBuilder<GroupContext>()
                    .UseMySql(
                        "Server=127.0.0.1;Port=1;Database=nexus_forever_group_test;User=test;Password=test;Connection Timeout=1;",
                        new MySqlServerVersion(new Version(8, 0, 36)))
                    .Options;
                context = new GroupContext(options)
                {
                    Character = CreateAsyncDbSet(new[]
                    {
                        CreateCharacter(LeaderId, inGroup: true),
                        CreateCharacter(MemberId, inGroup: true),
                        CreateCharacter(CandidateId, inGroup: false),
                    })
                };

                var services = new ServiceCollection();
                services.AddSingleton(context);
                httpClient = new HttpClient(new UnexpectedHttpMessageHandler())
                {
                    BaseAddress = new Uri("http://group-tests.invalid"),
                };
                services.AddSingleton(new CharacterAPIClient(httpClient));
                services.AddScoped<CharacterRepository>();
                services.AddScoped<GroupRepository>();
                services.AddScoped<InternalMessageRepository>();
                services.AddTransient<OutboxMessagePublisher>();
                services.AddGroup();
                services.AddCharacter();

                provider = services.BuildServiceProvider();
                scope = provider.CreateScope();
            }

            public GroupEntity CreateGroup(
                ulong revision = InitialRevision,
                GroupFlags flags = GroupFlags.None)
            {
                var model = new GroupModel
                {
                    GroupId          = GroupId,
                    Revision         = revision,
                    Flags            = flags,
                    LootRule         = LootRule.NeedBeforeGreed,
                    LootRuleThreshold = LootRule.NeedBeforeGreed,
                    LootThreshold    = LootThreshold.Good,
                    LootRuleHarvest  = HarvestLootRule.FirstTagger,
                    Leader           = new GroupLeaderModel
                    {
                        GroupId     = GroupId,
                        CharacterId = LeaderId,
                        RealmId     = RealmId,
                    },
                    Members =
                    [
                        new GroupMemberModel
                        {
                            GroupId     = GroupId,
                            CharacterId = LeaderId,
                            RealmId     = RealmId,
                            Index       = 1u,
                            Flags       = GroupMemberInfoFlags.GroupAdminFlags,
                        },
                        new GroupMemberModel
                        {
                            GroupId     = GroupId,
                            CharacterId = MemberId,
                            RealmId     = RealmId,
                            Index       = 2u,
                            Flags       = GroupMemberInfoFlags.GroupMemberFlags,
                        },
                    ],
                };

                GroupEntity group = scope.ServiceProvider.GetRequiredService<GroupEntity>();
                group.Initialise(model);
                return group;
            }

            public T SingleMessage<T>()
            {
                return Assert.Single(Messages<T>());
            }

            public T[] Messages<T>()
            {
                return PendingMessages
                    .Where(message => message.Type == typeof(T).AssemblyQualifiedName)
                    .Select(message => JsonSerializer.Deserialize<T>(message.Payload))
                    .ToArray();
            }

            public void Dispose()
            {
                scope.Dispose();
                provider.Dispose();
                httpClient.Dispose();
                context.Dispose();
            }

            private static CharacterModel CreateCharacter(ulong id, bool inGroup)
            {
                var model = new CharacterModel
                {
                    CharacterId = id,
                    RealmId     = RealmId,
                    Name        = $"Character{id}",
                    RealmName   = "TestRealm",
                    CurrentRealm = RealmId,
                };

                if (inGroup)
                {
                    model.Groups.Add(new CharacterGroupModel
                    {
                        CharacterId = id,
                        RealmId     = RealmId,
                        GroupId     = GroupId,
                        Index       = 0u,
                    });
                }

                return model;
            }
        }

        private static DbSet<T> CreateAsyncDbSet<T>(IEnumerable<T> items)
            where T : class
        {
            var data = new TestAsyncEnumerable<T>(items);
            var set = new Mock<DbSet<T>>();

            set.As<IAsyncEnumerable<T>>()
                .Setup(value => value.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken cancellationToken) => data.GetAsyncEnumerator(cancellationToken));
            set.As<IQueryable<T>>().Setup(value => value.Provider).Returns(((IQueryable<T>)data).Provider);
            set.As<IQueryable<T>>().Setup(value => value.Expression).Returns(((IQueryable<T>)data).Expression);
            set.As<IQueryable<T>>().Setup(value => value.ElementType).Returns(((IQueryable<T>)data).ElementType);
            set.As<IQueryable<T>>().Setup(value => value.GetEnumerator()).Returns(() => data.AsEnumerable().GetEnumerator());

            return set.Object;
        }

        private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
        {
            private readonly IQueryProvider inner;

            public TestAsyncQueryProvider(IQueryProvider inner)
            {
                this.inner = inner;
            }

            public IQueryable CreateQuery(Expression expression)
            {
                return new TestAsyncEnumerable<TEntity>(expression);
            }

            public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            {
                return new TestAsyncEnumerable<TElement>(expression);
            }

            public object Execute(Expression expression)
            {
                return inner.Execute(expression);
            }

            public TResult Execute<TResult>(Expression expression)
            {
                return inner.Execute<TResult>(expression);
            }

            public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
            {
                Type resultType = typeof(TResult).GetGenericArguments().Single();
                object result = Execute(expression);
                return (TResult)typeof(Task)
                    .GetMethod(nameof(Task.FromResult))
                    .MakeGenericMethod(resultType)
                    .Invoke(null, new[] { result });
            }
        }

        private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
        {
            public TestAsyncEnumerable(IEnumerable<T> enumerable)
                : base(enumerable)
            {
            }

            public TestAsyncEnumerable(Expression expression)
                : base(expression)
            {
            }

            IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);

            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new TestAsyncEnumerator<T>(((IEnumerable<T>)this).GetEnumerator());
            }
        }

        private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
        {
            private readonly IEnumerator<T> inner;

            public T Current => inner.Current;

            public TestAsyncEnumerator(IEnumerator<T> inner)
            {
                this.inner = inner;
            }

            public ValueTask<bool> MoveNextAsync()
            {
                return ValueTask.FromResult(inner.MoveNext());
            }

            public ValueTask DisposeAsync()
            {
                inner.Dispose();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class UnexpectedHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Character API fallback was not expected in producer tests.");
            }
        }
    }
}
