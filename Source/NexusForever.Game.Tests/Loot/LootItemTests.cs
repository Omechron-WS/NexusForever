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
        public void GetDrop_FullUIntRangeUsesInclusiveInt64Bounds()
        {
            long actualMinimum = 0L;
            long actualMaximum = 0L;
            var model = CreateModel(
                probability: 100f,
                minCount: uint.MaxValue - 1u,
                maxCount: uint.MaxValue);
            var item = new LootItem(model, (minimum, maximum) =>
            {
                actualMinimum = minimum;
                actualMaximum = maximum;
                return maximum - 1L;
            });

            bool dropped = item.GetDrop(out uint count);

            Assert.True(dropped);
            Assert.Equal((long)uint.MaxValue - 1L, actualMinimum);
            Assert.Equal((long)uint.MaxValue + 1L, actualMaximum);
            Assert.Equal(uint.MaxValue, count);
        }

        [Theory]
        [InlineData(0.499d, true)]
        [InlineData(0.5d, false)]
        public void GetDrop_ProbabilityBoundaryIsDeterministic(double probabilityRoll, bool expected)
        {
            var model = CreateModel(probability: 50f);
            var item = new LootItem(
                model,
                () => probabilityRoll,
                static (minimum, _) => minimum);

            bool dropped = item.GetDrop(out uint count);

            Assert.Equal(expected, dropped);
            Assert.Equal(expected ? 1u : 0u, count);
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

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(-1f)]
        [InlineData(100.01f)]
        public void Constructor_InvalidProbabilityIsRejected(float probability)
        {
            LootItemModel model = CreateModel(probability: probability);

            Assert.Throws<ArgumentException>(() => new LootItem(model));
        }

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(0u, 1u)]
        [InlineData(2u, 1u)]
        public void Constructor_InvalidCountRangeIsRejected(uint minCount, uint maxCount)
        {
            LootItemModel model = CreateModel(minCount: minCount, maxCount: maxCount);

            Assert.Throws<ArgumentException>(() => new LootItem(model));
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
