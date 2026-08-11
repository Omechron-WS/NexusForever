using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Group;
using NexusForever.Game.Group;
using NexusForever.Game.Static.Group;
using InternalGroup = NexusForever.Network.Internal.Message.Group.Shared.Group;
using InternalGroupMember = NexusForever.Network.Internal.Message.Group.Shared.GroupMember;
using InternalIdentity = NexusForever.Network.Internal.Message.Shared.Identity;

namespace NexusForever.Game.Tests.Group
{
    public class GroupSnapshotCacheTests
    {
        [Fact]
        public void TryUpsertCreatesDeeplyImmutableSnapshot()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup();

            Assert.True(cache.TryUpsert(source));
            Assert.True(cache.TryGet(source.Id, out GroupSnapshot snapshot));

            source.Flags = GroupFlags.Raid;
            source.NormalRule = LootRule.Master;
            source.Leader = Identity(99ul);
            source.Members[0].Identity = Identity(98ul);
            source.Members[0].Flags = GroupMemberInfoFlags.Disconnected;
            source.Members.Clear();

            Assert.Equal(GroupFlags.OpenWorld, snapshot.Flags);
            Assert.Equal(1ul, snapshot.Revision);
            Assert.Equal(LootRule.RoundRobin, snapshot.NormalRule);
            Assert.Equal(1ul, snapshot.LeaderCharacterId);
            Assert.Equal(2, snapshot.Members.Length);
            Assert.Equal(1ul, snapshot.Members[0].CharacterId);
            Assert.Equal(GroupMemberInfoFlags.CanInvite, snapshot.Members[0].Flags);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        public void NewerInvalidUpdateEvictsAndPoisonsRevision(int invalidField)
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup(revision: 1ul);
            Assert.True(cache.TryUpsert(source));

            InternalGroup invalid = CreateGroup(revision: 2ul);
            switch (invalidField)
            {
                case 0:
                    invalid.Flags = (GroupFlags)(1u << 31);
                    break;
                case 1:
                    invalid.NormalRule = (LootRule)byte.MaxValue;
                    break;
                case 2:
                    invalid.ThresholdRule = (LootRule)byte.MaxValue;
                    break;
                case 3:
                    invalid.ThresholdQuality = (LootThreshold)byte.MaxValue;
                    break;
                case 4:
                    invalid.Members[0].Flags = (GroupMemberInfoFlags)(1u << 31);
                    break;
                case 5:
                    invalid.HarvestRule = (HarvestLootRule)byte.MaxValue;
                    break;
                case 6:
                    invalid.Leader = Identity(99ul);
                    break;
                case 7:
                    invalid.Members[1].Identity = invalid.Members[0].Identity;
                    break;
                case 8:
                    invalid.Members[1].GroupIndex = invalid.Members[0].GroupIndex;
                    break;
                case 9:
                    invalid.Members[0].Identity = Identity(0ul);
                    break;
                case 10:
                    invalid.Members[0].Identity = new InternalIdentity
                    {
                        Id      = 1ul,
                        RealmId = 0,
                    };
                    break;
                case 11:
                    invalid.Members[0].GroupIndex = 0u;
                    break;
                case 12:
                    invalid.Members[0] = null;
                    break;
                case 13:
                    invalid.Members = null;
                    break;
                case 14:
                    invalid.Leader = null;
                    break;
            }

            Assert.False(cache.TryUpsert(invalid));
            Assert.False(cache.TryGet(source.Id, out _));
            Assert.False(cache.TryUpsert(source));
            Assert.False(cache.TryUpsert(CreateGroup(revision: 2ul)));
            Assert.True(cache.TryUpsert(CreateGroup(revision: 3ul)));
        }

        [Fact]
        public void StaleMalformedUpdateCannotEvictNewerSnapshot()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup current = CreateGroup(revision: 5ul);
            Assert.True(cache.TryUpsert(current));
            Assert.True(cache.TryGet(current.Id, out GroupSnapshot expected));

            InternalGroup stale = CreateGroup(revision: 4ul);
            stale.Flags = (GroupFlags)(1u << 31);

            Assert.False(cache.TryUpsert(stale));
            Assert.True(cache.TryGet(current.Id, out GroupSnapshot actual));
            Assert.Same(expected, actual);
        }

        [Fact]
        public void ZeroRevisionIsRejectedWithoutEvictingCurrentSnapshot()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup current = CreateGroup(revision: 2ul);
            Assert.True(cache.TryUpsert(current));
            Assert.True(cache.TryGet(current.Id, out GroupSnapshot expected));

            Assert.False(cache.TryUpsert(CreateGroup(revision: 0ul)));
            Assert.True(cache.TryGet(current.Id, out GroupSnapshot actual));
            Assert.Same(expected, actual);
        }

        [Fact]
        public void EqualEquivalentRevisionIsIdempotentButConflictPoisonsRevision()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup(revision: 7ul);
            Assert.True(cache.TryUpsert(source));

            InternalGroup equivalent = CreateGroup(revision: 7ul);
            equivalent.Members.Reverse();
            Assert.True(cache.TryUpsert(equivalent));

            InternalGroup conflicting = CreateGroup(revision: 7ul, normalRule: LootRule.Master);
            Assert.False(cache.TryUpsert(conflicting));
            Assert.False(cache.TryGet(source.Id, out _));
            Assert.False(cache.TryUpsert(source));

            Assert.True(cache.TryUpsert(CreateGroup(revision: 8ul, normalRule: LootRule.Master)));
            Assert.True(cache.TryGet(source.Id, out GroupSnapshot recovered));
            Assert.Equal(8ul, recovered.Revision);
            Assert.Equal(LootRule.Master, recovered.NormalRule);
        }

        [Fact]
        public void RevisionGapsAreAllowedAndStaleUpdatesCannotRegressState()
        {
            var cache = new GroupSnapshotCache();
            Assert.True(cache.TryUpsert(CreateGroup(revision: 1ul)));
            Assert.True(cache.TryUpsert(CreateGroup(revision: 100ul, normalRule: LootRule.Master)));

            Assert.False(cache.TryUpsert(CreateGroup(revision: 50ul, normalRule: LootRule.FreeForAll)));

            Assert.True(cache.TryGet(42ul, out GroupSnapshot snapshot));
            Assert.Equal(100ul, snapshot.Revision);
            Assert.Equal(LootRule.Master, snapshot.NormalRule);
        }

        [Fact]
        public void TryApplyMemberRemovalSubtractsMemberAndIsIdempotent()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup();
            InternalIdentity removed = source.Members[1].Identity;
            Assert.True(cache.TryUpsert(source));
            source.Revision = 2ul;

            Assert.True(cache.TryApplyMemberRemoval(source, removed));
            Assert.True(cache.TryApplyMemberRemoval(source, removed));

            Assert.True(cache.TryGet(source.Id, out GroupSnapshot snapshot));
            Assert.Equal(2ul, snapshot.Revision);
            GroupMemberSnapshot member = Assert.Single(snapshot.Members);
            Assert.Equal(1ul, member.CharacterId);
            Assert.Equal(2, source.Members.Count);
        }

        [Fact]
        public void InconsistentLeaderRemovalEvictsSnapshot()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup();
            Assert.True(cache.TryUpsert(source));
            source.Revision = 2ul;

            Assert.False(cache.TryApplyMemberRemoval(source, source.Leader));

            Assert.False(cache.TryGet(source.Id, out _));
            Assert.False(cache.TryUpsert(source));
            source.Revision = 3ul;
            Assert.True(cache.TryUpsert(source));
        }

        [Fact]
        public void StaleMalformedRemovalCannotEvictNewerSnapshot()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup current = CreateGroup(revision: 5ul);
            Assert.True(cache.TryUpsert(current));
            Assert.True(cache.TryGet(current.Id, out GroupSnapshot expected));

            InternalGroup stale = CreateGroup(revision: 4ul);
            Assert.False(cache.TryApplyMemberRemoval(stale, Identity(99ul)));

            Assert.True(cache.TryGet(current.Id, out GroupSnapshot actual));
            Assert.Same(expected, actual);
        }

        [Fact]
        public void DisbandRevisionBarrierRejectsLaterFullSnapshots()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup();
            Assert.True(cache.TryUpsert(source));

            Assert.True(cache.MarkDisbanded(source.Id, 2ul));
            Assert.True(cache.MarkDisbanded(source.Id, 2ul));
            Assert.False(cache.MarkDisbanded(source.Id, 1ul));
            Assert.False(cache.TryUpsert(source));
            source.Revision = 3ul;
            Assert.False(cache.TryUpsert(source));

            Assert.False(cache.TryGet(source.Id, out _));
        }

        [Fact]
        public void StaleDisbandCannotEvictNewerSnapshot()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup current = CreateGroup(revision: 5ul);
            Assert.True(cache.TryUpsert(current));
            Assert.True(cache.TryGet(current.Id, out GroupSnapshot expected));

            Assert.False(cache.MarkDisbanded(current.Id, 4ul));

            Assert.True(cache.TryGet(current.Id, out GroupSnapshot actual));
            Assert.Same(expected, actual);
        }

        [Fact]
        public void RevisionBarriersEvictOldestAtConfiguredCapacity()
        {
            var cache = new GroupSnapshotCache(3);

            Assert.True(cache.MarkDisbanded(1ul, 1ul));
            Assert.True(cache.MarkDisbanded(1ul, 1ul));
            Assert.True(cache.MarkDisbanded(2ul, 1ul));
            Assert.True(cache.MarkDisbanded(3ul, 1ul));
            Assert.False(cache.TryUpsert(CreateGroup(groupId: 1ul, revision: 2ul)));

            Assert.True(cache.MarkDisbanded(4ul, 1ul));

            Assert.True(cache.TryUpsert(CreateGroup(groupId: 1ul, revision: 2ul)));
            Assert.False(cache.TryUpsert(CreateGroup(groupId: 2ul, revision: 2ul)));
            Assert.False(cache.TryUpsert(CreateGroup(groupId: 4ul, revision: 2ul)));
        }

        [Fact]
        public async Task ConcurrentDisbandAndUpsertCannotRestoreTerminalGroup()
        {
            var cache = new GroupSnapshotCache(3);
            InternalGroup source = CreateGroup();
            Assert.True(cache.TryUpsert(source));

            using var start = new ManualResetEventSlim(false);
            Task writer = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < 10_000; i++)
                    cache.TryUpsert(source);
            });
            Task disbander = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < 1_000; i++)
                    Assert.True(cache.MarkDisbanded(source.Id, 2ul));
            });

            start.Set();
            await Task.WhenAll(writer, disbander);

            Assert.False(cache.TryUpsert(source));
            Assert.False(cache.TryGet(source.Id, out _));
        }

        [Fact]
        public async Task ConcurrentReadsAndUpsertsOnlyExposeCompleteSnapshots()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup first = CreateGroup();
            InternalGroup second = CreateGroup(
                revision: 2ul,
                flags: GroupFlags.Raid,
                normalRule: LootRule.Master,
                leaderId: 3ul,
                memberIds: [3ul, 4ul, 5ul]);
            Assert.True(cache.TryUpsert(first));

            using var start = new ManualResetEventSlim(false);
            Task firstWriter = Task.Run(() => WriteSnapshots(cache, first, start));
            Task secondWriter = Task.Run(() => WriteSnapshots(cache, second, start));
            Task firstReader = Task.Run(() => ReadSnapshots(cache, first.Id, start));
            Task secondReader = Task.Run(() => ReadSnapshots(cache, first.Id, start));

            start.Set();
            await Task.WhenAll(firstWriter, secondWriter, firstReader, secondReader);

            Assert.True(cache.TryGet(first.Id, out GroupSnapshot final));
            Assert.Equal(2ul, final.Revision);
        }

        [Fact]
        public void AddGameGroupRegistersSingleSharedCache()
        {
            var services = new ServiceCollection();
            services.AddGameGroup();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            GroupSnapshotCache writable = serviceProvider.GetRequiredService<GroupSnapshotCache>();
            IGroupSnapshotCache readable = serviceProvider.GetRequiredService<IGroupSnapshotCache>();

            Assert.Same(writable, readable);
        }

        private static void WriteSnapshots(
            GroupSnapshotCache cache,
            InternalGroup source,
            ManualResetEventSlim start)
        {
            start.Wait();
            for (int i = 0; i < 5_000; i++)
                cache.TryUpsert(source);
        }

        private static void ReadSnapshots(
            GroupSnapshotCache cache,
            ulong groupId,
            ManualResetEventSlim start)
        {
            start.Wait();
            for (int i = 0; i < 5_000; i++)
            {
                Assert.True(cache.TryGet(groupId, out GroupSnapshot snapshot));

                bool isFirst = snapshot.Flags == GroupFlags.OpenWorld
                    && snapshot.NormalRule == LootRule.RoundRobin
                    && snapshot.LeaderCharacterId == 1ul
                    && snapshot.Members.Length == 2;
                bool isSecond = snapshot.Flags == GroupFlags.Raid
                    && snapshot.NormalRule == LootRule.Master
                    && snapshot.LeaderCharacterId == 3ul
                    && snapshot.Members.Length == 3;

                Assert.True(isFirst || isSecond);
            }
        }

        private static InternalGroup CreateGroup(
            ulong groupId = 42ul,
            ulong revision = 1ul,
            GroupFlags flags = GroupFlags.OpenWorld,
            LootRule normalRule = LootRule.RoundRobin,
            ulong leaderId = 1ul,
            ulong[] memberIds = null)
        {
            memberIds ??= [1ul, 2ul];

            return new InternalGroup
            {
                Id               = groupId,
                Revision         = revision,
                Flags            = flags,
                NormalRule       = normalRule,
                ThresholdRule    = LootRule.NeedBeforeGreed,
                ThresholdQuality = LootThreshold.Good,
                HarvestRule      = HarvestLootRule.FirstTagger,
                Leader           = Identity(leaderId),
                Members          = memberIds.Select((id, index) => new InternalGroupMember
                {
                    Identity   = Identity(id),
                    GroupIndex = (uint)index + 1u,
                    Flags      = index == 0 ? GroupMemberInfoFlags.CanInvite : GroupMemberInfoFlags.None,
                }).ToList(),
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
