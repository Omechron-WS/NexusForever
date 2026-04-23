using NexusForever.Database.World.Model;
using NexusForever.Game.Loot;
using NexusForever.Game.Static.Loot;

namespace NexusForever.Game.Tests.Loot
{
    public class LootItemTests
    {
        [Fact]
        public void GetDrop_WithZeroProbability_NeverDrops()
        {
            var model = CreateModel(probability: 0f, minCount: 1, maxCount: 5);
            var item = new LootItem(model);

            for (int i = 0; i < 100; i++)
            {
                bool dropped = item.GetDrop(out uint count);
                Assert.False(dropped);
                Assert.Equal(0u, count);
            }
        }

        [Fact]
        public void GetDrop_With100Probability_AlwaysDrops()
        {
            var model = CreateModel(probability: 100f, minCount: 1, maxCount: 1);
            var item = new LootItem(model);

            for (int i = 0; i < 100; i++)
            {
                bool dropped = item.GetDrop(out uint count);
                Assert.True(dropped);
                Assert.Equal(1u, count);
            }
        }

        [Fact]
        public void GetDrop_CountWithinRange()
        {
            var model = CreateModel(probability: 100f, minCount: 5, maxCount: 10);
            var item = new LootItem(model);

            for (int i = 0; i < 200; i++)
            {
                item.GetDrop(out uint count);
                Assert.InRange(count, 5u, 10u);
            }
        }

        [Fact]
        public void GetDrop_EqualMinMaxReturnsExactCount()
        {
            var model = CreateModel(probability: 100f, minCount: 7, maxCount: 7);
            var item = new LootItem(model);

            for (int i = 0; i < 50; i++)
            {
                item.GetDrop(out uint count);
                Assert.Equal(7u, count);
            }
        }

        [Fact]
        public void Constructor_SetsTypeAndStaticId()
        {
            var model = CreateModel(type: (uint)LootItemType.AccountCurrency, staticId: 42);
            var item = new LootItem(model);

            Assert.Equal(LootItemType.AccountCurrency, item.Type);
            Assert.Equal(42u, item.StaticId);
        }

        [Fact]
        public void GetDrop_PartialProbability_SometimesDrops()
        {
            var model = CreateModel(probability: 50f, minCount: 1, maxCount: 1);
            var item = new LootItem(model);

            int drops = 0;
            int iterations = 10000;
            for (int i = 0; i < iterations; i++)
                if (item.GetDrop(out _))
                    drops++;

            // With 50% probability over 10k iterations, expect roughly 5000
            // Allow wide margin (40-60%) to avoid flaky test
            Assert.InRange(drops, iterations * 0.35, iterations * 0.65);
        }

        private static LootItemModel CreateModel(
            float probability = 100f,
            uint minCount = 1,
            uint maxCount = 1,
            uint type = 0,
            uint staticId = 100)
        {
            return new LootItemModel
            {
                Id          = 1,
                Type        = type,
                StaticId    = staticId,
                Probability = probability,
                MinCount    = minCount,
                MaxCount    = maxCount,
                Comment     = ""
            };
        }
    }
}
