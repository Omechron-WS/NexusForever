using System.Collections.Generic;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Loot;
using NexusForever.Game.Static.Loot;
using Moq;

namespace NexusForever.Game.Tests.Loot
{
    public class LootGroupTests
    {
        [Fact]
        public void GenerateLootDrops_WithZeroProbability_ReturnsEmpty()
        {
            var model = CreateGroupModel(probability: 0f, items: new[]
            {
                CreateItemModel(probability: 100f)
            });

            var group = new LootGroup(model);
            var player = CreateMockPlayer();

            var drops = group.GenerateLootDrops(player);
            Assert.Empty(drops);
        }

        [Fact]
        public void GenerateLootDrops_With100Probability_ReturnsItems()
        {
            var model = CreateGroupModel(probability: 100f, items: new[]
            {
                CreateItemModel(probability: 100f, staticId: 1001)
            });

            var group = new LootGroup(model);
            var player = CreateMockPlayer();

            var drops = group.GenerateLootDrops(player);
            Assert.NotEmpty(drops);
            Assert.Contains(drops, kv => kv.Key.StaticId == 1001);
        }

        [Fact]
        public void GenerateLootDrops_RespectsMaxDrop()
        {
            var model = CreateGroupModel(probability: 100f, maxDrop: 2, items: new[]
            {
                CreateItemModel(probability: 100f, staticId: 1),
                CreateItemModel(probability: 100f, staticId: 2),
                CreateItemModel(probability: 100f, staticId: 3),
                CreateItemModel(probability: 100f, staticId: 4),
                CreateItemModel(probability: 100f, staticId: 5)
            });

            var group = new LootGroup(model);
            var player = CreateMockPlayer();

            for (int i = 0; i < 50; i++)
            {
                var drops = group.GenerateLootDrops(player);
                Assert.True(drops.Count <= 2, $"Expected at most 2 drops, got {drops.Count}");
            }
        }

        [Fact]
        public void GenerateLootDrops_RespectsMinDrop()
        {
            // minDrop=2 with 100% items — should always get at least 2
            var model = CreateGroupModel(probability: 100f, minDrop: 2, maxDrop: 5, items: new[]
            {
                CreateItemModel(probability: 100f, staticId: 1),
                CreateItemModel(probability: 100f, staticId: 2),
                CreateItemModel(probability: 100f, staticId: 3)
            });

            var group = new LootGroup(model);
            var player = CreateMockPlayer();

            for (int i = 0; i < 50; i++)
            {
                var drops = group.GenerateLootDrops(player);
                Assert.True(drops.Count >= 2, $"Expected at least 2 drops, got {drops.Count}");
            }
        }

        [Fact]
        public void GenerateLootDrops_ChildGroupsContribute()
        {
            var childModel = CreateGroupModel(id: 2, probability: 100f, items: new[]
            {
                CreateItemModel(probability: 100f, staticId: 2001)
            });

            var parentModel = CreateGroupModel(id: 1, probability: 100f, items: new[]
            {
                CreateItemModel(probability: 100f, staticId: 1001)
            });
            parentModel.ChildGroup = new HashSet<LootGroupModel> { childModel };

            var group = new LootGroup(parentModel);
            var player = CreateMockPlayer();

            var drops = group.GenerateLootDrops(player);
            Assert.True(drops.Count >= 2);
            Assert.Contains(drops, kv => kv.Key.StaticId == 1001);
            Assert.Contains(drops, kv => kv.Key.StaticId == 2001);
        }

        [Fact]
        public void GenerateLootDrops_ClampsMaxDropWhenLessThanMinDrop()
        {
            // maxDrop < minDrop — constructor should clamp maxDrop = minDrop
            var model = CreateGroupModel(probability: 100f, minDrop: 3, maxDrop: 1, items: new[]
            {
                CreateItemModel(probability: 100f, staticId: 1),
                CreateItemModel(probability: 100f, staticId: 2),
                CreateItemModel(probability: 100f, staticId: 3)
            });

            var group = new LootGroup(model);
            var player = CreateMockPlayer();

            var drops = group.GenerateLootDrops(player);
            Assert.Equal(3, drops.Count);
        }

        private static IPlayer CreateMockPlayer()
        {
            var mockQuestManager = new Mock<IQuestManager>();
            var mockPlayer = new Mock<IPlayer>();
            mockPlayer.Setup(p => p.QuestManager).Returns(mockQuestManager.Object);
            mockPlayer.Setup(p => p.Level).Returns(50u);
            return mockPlayer.Object;
        }

        private static LootGroupModel CreateGroupModel(
            ulong id = 1,
            float probability = 100f,
            uint minDrop = 0,
            uint maxDrop = 0,
            LootItemModel[] items = null)
        {
            var model = new LootGroupModel
            {
                Id            = id,
                ParentId      = null,
                Probability   = probability,
                MinDrop       = minDrop,
                MaxDrop       = maxDrop,
                ConditionType = 0,
                Condition     = 0,
                Comment       = "",
                ChildGroup    = new HashSet<LootGroupModel>(),
                Item          = new HashSet<LootItemModel>(items ?? System.Array.Empty<LootItemModel>())
            };
            return model;
        }

        private static LootItemModel CreateItemModel(
            float probability = 100f,
            uint staticId = 100,
            uint type = 0)
        {
            return new LootItemModel
            {
                Id          = staticId,
                Type        = type,
                StaticId    = staticId,
                Probability = probability,
                MinCount    = 1,
                MaxCount    = 1,
                Comment     = ""
            };
        }
    }
}
