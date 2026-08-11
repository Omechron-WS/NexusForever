using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Group;
using NexusForever.Game.Group;
using NexusForever.Game.Static.Group;
using NexusForever.Network.Internal.Message.Group;
using NexusForever.Network.Internal.Message.Player;
using NexusForever.WorldServer.Network.Internal.Handler;
using NexusForever.WorldServer.Network.Internal.Handler.Group;
using Rebus.Handlers;
using InternalGroup = NexusForever.Network.Internal.Message.Group.Shared.Group;
using InternalGroupMember = NexusForever.Network.Internal.Message.Group.Shared.GroupMember;
using InternalIdentity = NexusForever.Network.Internal.Message.Shared.Identity;

namespace NexusForever.WorldServer.Tests
{
    public class GroupSnapshotHandlerTests
    {
        [Fact]
        public void AddNetworkInternalHandlersRegistersDisbandSnapshotHandler()
        {
            var services = new ServiceCollection();
            services.AddGameGroup();
            services.AddNetworkInternalHandlers();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IHandleMessages<GroupDisbandedMessage> handler = Assert.Single(
                serviceProvider.GetServices<IHandleMessages<GroupDisbandedMessage>>());

            Assert.IsType<GroupSnapshotHandler>(handler);
        }

        [Fact]
        public async Task FullSnapshotMessagesUpsertAuthoritativeState()
        {
            var cache = new GroupSnapshotCache();
            var handler = new GroupSnapshotHandler(cache);
            InternalGroup group = CreateGroup();

            await handler.Handle(new PlayerGroupAssociationUpdatedMessage { Group = group });
            AssertSnapshot(cache, group.Id, 1ul, LootRule.RoundRobin);

            group.NormalRule = LootRule.FreeForAll;
            group.Revision = 2ul;
            await handler.Handle(new GroupMemberAddedMessage { Group = group });
            AssertSnapshot(cache, group.Id, 2ul, LootRule.FreeForAll);

            group.NormalRule = LootRule.NeedBeforeGreed;
            group.Revision = 3ul;
            await handler.Handle(new GroupMemberJoinedMessage { Group = group });
            AssertSnapshot(cache, group.Id, 3ul, LootRule.NeedBeforeGreed);

            group.NormalRule = LootRule.Master;
            group.Revision = 4ul;
            await handler.Handle(new GroupMemberPromotedMessage { Group = group });
            AssertSnapshot(cache, group.Id, 4ul, LootRule.Master);

            group.NormalRule = LootRule.RoundRobin;
            group.Revision = 5ul;
            await handler.Handle(new GroupFlagsUpdatedMessage { Group = group });
            await handler.Handle(new GroupMemberFlagsUpdatedMessage { Group = group });
            await handler.Handle(new GroupLootRulesUpdatedMessage { Group = group });
            AssertSnapshot(cache, group.Id, 5ul, LootRule.RoundRobin);
        }

        [Fact]
        public async Task RemovedAndLeftMessagesSubtractMemberStillPresentInPayload()
        {
            var cache = new GroupSnapshotCache();
            var handler = new GroupSnapshotHandler(cache);
            InternalGroup group = CreateGroup();
            InternalGroupMember removed = group.Members[1];

            await handler.Handle(new GroupMemberRemovedMessage
            {
                Group         = group,
                RemovedMember = removed,
                Reason        = RemoveReason.Kicked,
            });
            AssertOnlyLeaderRemains(cache, group.Id);

            group.Revision = 2ul;
            Assert.True(cache.TryUpsert(group));
            group.Revision = 3ul;
            await handler.Handle(new GroupMemberLeftMessage
            {
                Group         = group,
                RemovedMember = removed,
                Reason        = RemoveReason.Left,
            });
            AssertOnlyLeaderRemains(cache, group.Id);
            Assert.Equal(2, group.Members.Count);
        }

        [Fact]
        public async Task DisbandFollowedByMemberLeftCannotResurrectSnapshot()
        {
            var cache = new GroupSnapshotCache();
            var handler = new GroupSnapshotHandler(cache);
            InternalGroup group = CreateGroup();
            Assert.True(cache.TryUpsert(group));

            group.Revision = 2ul;
            await handler.Handle(new GroupDisbandedMessage { Group = group });
            await handler.Handle(new GroupMemberLeftMessage
            {
                Group         = group,
                RemovedMember = group.Members[0],
                Reason        = RemoveReason.Disband,
            });
            await handler.Handle(new GroupMemberLeftMessage
            {
                Group         = group,
                RemovedMember = group.Members[1],
                Reason        = RemoveReason.Disband,
            });
            await handler.Handle(new GroupFlagsUpdatedMessage { Group = group });
            await handler.Handle(new PlayerGroupAssociationUpdatedMessage { Group = group });

            Assert.False(cache.TryGet(group.Id, out _));
        }

        [Fact]
        public async Task InconsistentRemovalEvictsSnapshot()
        {
            var cache = new GroupSnapshotCache();
            var handler = new GroupSnapshotHandler(cache);
            InternalGroup group = CreateGroup();
            Assert.True(cache.TryUpsert(group));
            group.Revision = 2ul;

            await handler.Handle(new GroupMemberRemovedMessage
            {
                Group = group,
                RemovedMember = new InternalGroupMember
                {
                    Identity   = Identity(99ul),
                    GroupIndex = 99u,
                },
                Reason = RemoveReason.Kicked,
            });

            Assert.False(cache.TryGet(group.Id, out _));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(8)]
        public async Task InvalidRemovalReasonEvictsLiveSnapshot(int reason)
        {
            var cache = new GroupSnapshotCache();
            var handler = new GroupSnapshotHandler(cache);
            InternalGroup group = CreateGroup();
            Assert.True(cache.TryUpsert(group));
            group.Revision = 2ul;

            await handler.Handle(new GroupMemberRemovedMessage
            {
                Group         = group,
                RemovedMember = group.Members[1],
                Reason        = (RemoveReason)reason,
            });

            Assert.False(cache.TryGet(group.Id, out _));
            Assert.False(cache.TryUpsert(group));
            group.Revision = 3ul;
            Assert.True(cache.TryUpsert(group));
        }

        [Fact]
        public async Task NullRemovedMemberEvictsLiveSnapshot()
        {
            var cache = new GroupSnapshotCache();
            var handler = new GroupSnapshotHandler(cache);
            InternalGroup group = CreateGroup();
            Assert.True(cache.TryUpsert(group));
            group.Revision = 2ul;

            await handler.Handle(new GroupMemberLeftMessage
            {
                Group         = group,
                RemovedMember = null,
                Reason        = RemoveReason.Left,
            });

            Assert.False(cache.TryGet(group.Id, out _));
        }

        [Fact]
        public async Task StaleMalformedRemovalCannotEvictNewerSnapshot()
        {
            var cache = new GroupSnapshotCache();
            var handler = new GroupSnapshotHandler(cache);
            InternalGroup current = CreateGroup();
            current.Revision = 5ul;
            Assert.True(cache.TryUpsert(current));
            Assert.True(cache.TryGet(current.Id, out GroupSnapshot expected));

            InternalGroup stale = CreateGroup();
            stale.Revision = 4ul;
            await handler.Handle(new GroupMemberRemovedMessage
            {
                Group         = stale,
                RemovedMember = stale.Members[1],
                Reason        = (RemoveReason)0,
            });

            Assert.True(cache.TryGet(current.Id, out GroupSnapshot actual));
            Assert.Same(expected, actual);
        }

        private static void AssertSnapshot(GroupSnapshotCache cache, ulong groupId, ulong revision, LootRule normalRule)
        {
            Assert.True(cache.TryGet(groupId, out GroupSnapshot snapshot));
            Assert.Equal(revision, snapshot.Revision);
            Assert.Equal(normalRule, snapshot.NormalRule);
            Assert.Equal(2, snapshot.Members.Length);
        }

        private static void AssertOnlyLeaderRemains(GroupSnapshotCache cache, ulong groupId)
        {
            Assert.True(cache.TryGet(groupId, out GroupSnapshot snapshot));
            GroupMemberSnapshot member = Assert.Single(snapshot.Members);
            Assert.Equal(snapshot.LeaderCharacterId, member.CharacterId);
            Assert.Equal(snapshot.LeaderRealmId, member.RealmId);
        }

        private static InternalGroup CreateGroup()
        {
            return new InternalGroup
            {
                Id               = 42ul,
                Revision         = 1ul,
                Flags            = GroupFlags.OpenWorld,
                NormalRule       = LootRule.RoundRobin,
                ThresholdRule    = LootRule.NeedBeforeGreed,
                ThresholdQuality = LootThreshold.Good,
                HarvestRule      = HarvestLootRule.FirstTagger,
                Leader           = Identity(1ul),
                Members =
                [
                    new InternalGroupMember
                    {
                        Identity   = Identity(1ul),
                        GroupIndex = 1u,
                    },
                    new InternalGroupMember
                    {
                        Identity   = Identity(2ul),
                        GroupIndex = 2u,
                    },
                ],
            };
        }

        private static InternalIdentity Identity(ulong characterId)
        {
            return new InternalIdentity
            {
                Id      = characterId,
                RealmId = 7,
            };
        }
    }
}
