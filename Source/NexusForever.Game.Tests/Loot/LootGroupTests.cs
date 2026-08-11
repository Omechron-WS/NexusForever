using System.Collections.Generic;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Loot;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Loot;
using NexusForever.Game.Static.Quest;
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
        public void Constructor_MaxDropLessThanMinDropIsRejected()
        {
            var model = CreateGroupModel(probability: 100f, minDrop: 3, maxDrop: 1, items: new[]
            {
                CreateItemModel(probability: 100f, staticId: 1),
                CreateItemModel(probability: 100f, staticId: 2),
                CreateItemModel(probability: 100f, staticId: 3)
            });

            Assert.Throws<ArgumentException>(() => new LootGroup(model));
        }

        [Theory]
        [InlineData(LootConditionType.None, 0u, Class.Warrior, Race.Human, 50u, true)]
        [InlineData(LootConditionType.IsClass, (uint)Class.Warrior, Class.Warrior, Race.Human, 50u, true)]
        [InlineData(LootConditionType.IsClass, (uint)Class.Warrior, Class.Esper, Race.Human, 50u, false)]
        [InlineData(LootConditionType.IsRace, (uint)Race.Human, Class.Warrior, Race.Human, 50u, true)]
        [InlineData(LootConditionType.IsRace, (uint)Race.Human, Class.Warrior, Race.Aurin, 50u, false)]
        [InlineData(LootConditionType.IsLevel, 50u, Class.Warrior, Race.Human, 50u, true)]
        [InlineData(LootConditionType.IsLevel, 49u, Class.Warrior, Race.Human, 50u, false)]
        [InlineData(LootConditionType.IsLessThanLevel, 51u, Class.Warrior, Race.Human, 50u, true)]
        [InlineData(LootConditionType.IsLessThanLevel, 50u, Class.Warrior, Race.Human, 50u, false)]
        [InlineData(LootConditionType.IsMoreThanLevel, 49u, Class.Warrior, Race.Human, 50u, true)]
        [InlineData(LootConditionType.IsMoreThanLevel, 50u, Class.Warrior, Race.Human, 50u, false)]
        public void GenerateLootDrops_ScalarConditionMatchesExpected(
            LootConditionType conditionType,
            uint condition,
            Class playerClass,
            Race playerRace,
            uint playerLevel,
            bool expected)
        {
            LootGroup group = CreateConditionalGroup(conditionType, condition);
            IPlayer player = CreateMockPlayer(playerClass, playerRace, playerLevel);

            Dictionary<ILootItem, uint> drops = group.GenerateLootDrops(player);

            Assert.Equal(expected, drops.Count == 1);
        }

        [Theory]
        [InlineData(LootConditionType.QuestIsComplete, QuestState.Completed, true)]
        [InlineData(LootConditionType.QuestIsComplete, QuestState.Accepted, false)]
        [InlineData(LootConditionType.QuestNotComplete, QuestState.Accepted, true)]
        [InlineData(LootConditionType.QuestNotComplete, QuestState.Completed, false)]
        public void GenerateLootDrops_QuestStateConditionMatchesExpected(
            LootConditionType conditionType,
            QuestState questState,
            bool expected)
        {
            const ushort questId = 42;
            var questManager = new Mock<IQuestManager>();
            questManager.Setup(q => q.GetQuestState(questId)).Returns(questState);
            LootGroup group = CreateConditionalGroup(conditionType, questId);
            IPlayer player = CreateMockPlayer(questManager: questManager.Object);

            Dictionary<ILootItem, uint> drops = group.GenerateLootDrops(player);

            Assert.Equal(expected, drops.Count == 1);
        }

        [Fact]
        public void GenerateLootDrops_QuestNotCompleteMatchesQuestThatWasNeverStarted()
        {
            const ushort questId = 42;
            var questManager = new Mock<IQuestManager>();
            questManager.Setup(q => q.GetQuestState(questId)).Returns((QuestState?)null);
            LootGroup group = CreateConditionalGroup(LootConditionType.QuestNotComplete, questId);
            IPlayer player = CreateMockPlayer(questManager: questManager.Object);

            Dictionary<ILootItem, uint> drops = group.GenerateLootDrops(player);

            Assert.Single(drops);
        }

        [Theory]
        [InlineData(77u, false, true)]
        [InlineData(77u, true, false)]
        [InlineData(78u, false, false)]
        public void GenerateLootDrops_QuestObjectiveActiveRequiresMatchingIncompleteObjective(
            uint objectiveId,
            bool isComplete,
            bool expected)
        {
            const uint condition = 77u;
            var objectiveInfo = new Mock<IQuestObjectiveInfo>();
            objectiveInfo.SetupGet(info => info.Id).Returns(objectiveId);
            var objective = new Mock<IQuestObjective>();
            objective.SetupGet(o => o.ObjectiveInfo).Returns(objectiveInfo.Object);
            objective.Setup(o => o.IsComplete()).Returns(isComplete);
            var quest = new Mock<IQuest>();
            quest.Setup(q => q.GetEnumerator())
                .Returns(() => new[] { objective.Object }.AsEnumerable().GetEnumerator());
            var questManager = new Mock<IQuestManager>();
            questManager.Setup(q => q.GetActiveQuests()).Returns(new[] { quest.Object });
            LootGroup group = CreateConditionalGroup(LootConditionType.QuestObjectiveActive, condition);
            IPlayer player = CreateMockPlayer(questManager: questManager.Object);

            Dictionary<ILootItem, uint> drops = group.GenerateLootDrops(player);

            Assert.Equal(expected, drops.Count == 1);
        }

        [Theory]
        [InlineData(LootConditionType.IsClass, 0u)]
        [InlineData(LootConditionType.IsClass, 255u)]
        [InlineData(LootConditionType.IsRace, 0u)]
        [InlineData(LootConditionType.IsRace, 255u)]
        [InlineData(LootConditionType.IsLevel, 0u)]
        [InlineData(LootConditionType.IsLessThanLevel, 0u)]
        [InlineData(LootConditionType.IsMoreThanLevel, 0u)]
        [InlineData(LootConditionType.QuestIsComplete, 0u)]
        [InlineData(LootConditionType.QuestIsComplete, 65536u)]
        [InlineData(LootConditionType.QuestNotComplete, 0u)]
        [InlineData(LootConditionType.QuestNotComplete, 65536u)]
        [InlineData(LootConditionType.QuestObjectiveActive, 0u)]
        public void GenerateLootDrops_InvalidConditionValueFailsClosed(
            LootConditionType conditionType,
            uint condition)
        {
            LootGroup group = CreateConditionalGroup(conditionType, condition);
            IPlayer player = CreateMockPlayer();

            Dictionary<ILootItem, uint> drops = group.GenerateLootDrops(player);

            Assert.Empty(drops);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(-1f)]
        [InlineData(100.01f)]
        public void Constructor_InvalidProbabilityIsRejected(float probability)
        {
            LootGroupModel model = CreateGroupModel(probability: probability, items: new[]
            {
                CreateItemModel(probability: 100f)
            });

            Assert.Throws<ArgumentException>(() => new LootGroup(model));
        }

        [Fact]
        public void GenerateLootDrops_UnknownConditionFailsClosed()
        {
            LootGroupModel model = CreateGroupModel(items: new[]
            {
                CreateItemModel(probability: 100f)
            });
            model.ConditionType = uint.MaxValue;
            var group = new LootGroup(model);

            Dictionary<ILootItem, uint> drops = group.GenerateLootDrops(CreateMockPlayer());

            Assert.Empty(drops);
        }

        [Fact]
        public void GenerateLootDrops_NullPlayerFailsClosed()
        {
            LootGroup group = CreateConditionalGroup(LootConditionType.None, 0u);

            Dictionary<ILootItem, uint> drops = group.GenerateLootDrops(null);

            Assert.Empty(drops);
        }

        [Theory]
        [InlineData(LootConditionType.QuestIsComplete)]
        [InlineData(LootConditionType.QuestNotComplete)]
        [InlineData(LootConditionType.QuestObjectiveActive)]
        public void GenerateLootDrops_MissingQuestManagerFailsClosed(LootConditionType conditionType)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(p => p.Level).Returns(50u);
            LootGroup group = CreateConditionalGroup(conditionType, 42u);

            Dictionary<ILootItem, uint> drops = group.GenerateLootDrops(player.Object);

            Assert.Empty(drops);
        }

        private static LootGroup CreateConditionalGroup(LootConditionType conditionType, uint condition)
        {
            LootGroupModel model = CreateGroupModel(items: new[]
            {
                CreateItemModel(probability: 100f)
            });
            model.ConditionType = (uint)conditionType;
            model.Condition = condition;
            return new LootGroup(model);
        }

        private static IPlayer CreateMockPlayer(
            Class playerClass = Class.Warrior,
            Race playerRace = Race.Human,
            uint playerLevel = 50u,
            IQuestManager questManager = null)
        {
            questManager ??= new Mock<IQuestManager>().Object;
            var mockPlayer = new Mock<IPlayer>();
            mockPlayer.SetupGet(p => p.Class).Returns(playerClass);
            mockPlayer.SetupGet(p => p.Race).Returns(playerRace);
            mockPlayer.SetupGet(p => p.Level).Returns(playerLevel);
            mockPlayer.SetupGet(p => p.QuestManager).Returns(questManager);
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
