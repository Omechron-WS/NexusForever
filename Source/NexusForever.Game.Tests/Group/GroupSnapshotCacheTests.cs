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
        public void InvalidUpdateRetainsPreviousSnapshot(int invalidField)
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup();
            Assert.True(cache.TryUpsert(source));
            Assert.True(cache.TryGet(source.Id, out GroupSnapshot expected));

            InternalGroup invalid = CreateGroup();
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
            Assert.True(cache.TryGet(source.Id, out GroupSnapshot actual));
            Assert.Same(expected, actual);
        }

        [Fact]
        public void TryApplyMemberRemovalSubtractsMemberAndIsIdempotent()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup();
            InternalIdentity removed = source.Members[1].Identity;
            Assert.True(cache.TryUpsert(source));

            Assert.True(cache.TryApplyMemberRemoval(source, removed));
            Assert.True(cache.TryApplyMemberRemoval(source, removed));

            Assert.True(cache.TryGet(source.Id, out GroupSnapshot snapshot));
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

            Assert.False(cache.TryApplyMemberRemoval(source, source.Leader));

            Assert.False(cache.TryGet(source.Id, out _));
            Assert.True(cache.TryUpsert(source));
        }

        [Fact]
        public void DisbandTombstoneRejectsLaterFullSnapshots()
        {
            var cache = new GroupSnapshotCache();
            InternalGroup source = CreateGroup();
            Assert.True(cache.TryUpsert(source));

            Assert.True(cache.MarkDisbanded(source.Id));
            Assert.True(cache.MarkDisbanded(source.Id));
            cache.Evict(source.Id);
            Assert.False(cache.TryUpsert(source));

            Assert.False(cache.TryGet(source.Id, out _));
        }

        [Fact]
        public void DisbandTombstonesEvictOldestAtConfiguredCapacity()
        {
            var cache = new GroupSnapshotCache(3);

            Assert.True(cache.MarkDisbanded(1ul));
            Assert.True(cache.MarkDisbanded(1ul));
            Assert.True(cache.MarkDisbanded(2ul));
            Assert.True(cache.MarkDisbanded(3ul));
            Assert.False(cache.TryUpsert(CreateGroup(groupId: 1ul)));

            Assert.True(cache.MarkDisbanded(4ul));

            Assert.True(cache.TryUpsert(CreateGroup(groupId: 1ul)));
            Assert.False(cache.TryUpsert(CreateGroup(groupId: 2ul)));
            Assert.False(cache.TryUpsert(CreateGroup(groupId: 4ul)));
        }

        [Fact]
        public async Task ConcurrentDisbandAndUpsertCannotRestoreTombstonedGroup()
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
                    Assert.True(cache.MarkDisbanded(source.Id));
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
                Assert.True(cache.TryUpsert(source));
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
            GroupFlags flags = GroupFlags.OpenWorld,
            LootRule normalRule = LootRule.RoundRobin,
            ulong leaderId = 1ul,
            ulong[] memberIds = null)
        {
            memberIds ??= [1ul, 2ul];

            return new InternalGroup
            {
                Id               = groupId,
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
