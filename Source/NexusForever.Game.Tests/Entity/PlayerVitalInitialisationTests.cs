using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Matching.Match;
using NexusForever.Game.Abstract.Matching.Queue;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Network.Internal;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    [Collection(VitalServiceProviderCollection.Name)]
    public sealed class PlayerVitalInitialisationTests : IDisposable
    {
        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public PlayerVitalInitialisationTests()
        {
            previousProvider = LegacyServiceProvider.Provider;

            var entityManager = new EntityManager();
            typeof(EntityManager)
                .GetMethod("InitialiseEntityStats", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(entityManager, null);

            serviceProvider = new ServiceCollection()
                .AddSingleton(entityManager)
                .BuildServiceProvider();
            LegacyServiceProvider.Provider = serviceProvider;
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        public static TheoryData<Stat, Property, float> ExistingVitalValues => new()
        {
            { Stat.Resource0, Property.ResourceMax0, 125f },
            { Stat.Focus, Property.BaseFocusPool, 0f },
            { Stat.Resource1, Property.ResourceMax1, 3f },
            { Stat.Resource2, Property.ResourceMax2, 0f },
            { Stat.Resource3, Property.ResourceMax3, 25f },
            { Stat.Resource4, Property.ResourceMax4, 0f },
            { Stat.Resource5, Property.ResourceMax5, 40f },
            { Stat.Resource6, Property.ResourceMax6, 0f },
            { Stat.Dash, Property.ResourceMax7, 75f }
        };

        [Fact]
        public void MissingRecoverableVitals_StartAtMaximum()
        {
            TestPlayer player = CreatePlayer();
            player.SetBaseProperty(Property.ResourceMax0, 500f);
            player.SetBaseProperty(Property.BaseFocusPool, 1000f);
            player.SetBaseProperty(Property.ResourceMax7, 200f);

            player.InitialiseMissingVitalStats();

            Assert.Equal(500f, player.GetFloatStat(Stat.Resource0));
            Assert.Equal(1000f, player.GetFloatStat(Stat.Focus));
            Assert.Equal(200f, player.GetFloatStat(Stat.Dash));
        }

        [Theory]
        [InlineData(Stat.Resource1, Property.ResourceMax1)]
        [InlineData(Stat.Resource2, Property.ResourceMax2)]
        [InlineData(Stat.Resource3, Property.ResourceMax3)]
        [InlineData(Stat.Resource4, Property.ResourceMax4)]
        [InlineData(Stat.Resource5, Property.ResourceMax5)]
        [InlineData(Stat.Resource6, Property.ResourceMax6)]
        public void MissingBuilderVital_WithPositiveMaximumStartsEmpty(Stat stat, Property maximumProperty)
        {
            TestPlayer player = CreatePlayer();
            player.SetBaseProperty(maximumProperty, 100f);

            player.InitialiseMissingVitalStats();

            Assert.True(player.HasStat(stat));
            Assert.Equal(0f, player.GetFloatStat(stat));
        }

        [Theory]
        [MemberData(nameof(ExistingVitalValues))]
        public void ExistingVitalValue_IsPreserved(Stat stat, Property maximumProperty, float existingValue)
        {
            TestPlayer player = CreatePlayer();
            player.SetBaseProperty(maximumProperty, 1000f);
            player.SetFloatStat(stat, existingValue);

            player.InitialiseMissingVitalStats();

            Assert.Equal(existingValue, player.GetFloatStat(stat));
        }

        [Theory]
        [InlineData(Stat.Focus, Property.BaseFocusPool)]
        [InlineData(Stat.Resource1, Property.ResourceMax1)]
        [InlineData(Stat.Resource3, Property.ResourceMax3)]
        [InlineData(Stat.Resource4, Property.ResourceMax4)]
        public void MissingVital_WithoutPositiveMaximumIsNotCreated(Stat stat, Property maximumProperty)
        {
            TestPlayer player = CreatePlayer();
            player.SetBaseProperty(maximumProperty, 0f);

            player.InitialiseMissingVitalStats();

            Assert.False(player.HasStat(stat));
        }

        private static TestPlayer CreatePlayer()
        {
            return new TestPlayer(
                new Mock<IMovementManager>().Object,
                new Mock<IInternalMessagePublisher>().Object,
                new Mock<IEntityFactory>().Object,
                new Mock<IMatchingManager>().Object,
                new Mock<IMatchManager>().Object,
                new Mock<ICurrencyManager>().Object,
                new Mock<IGuildManager>().Object,
                new Mock<IResidenceManager>().Object);
        }

        private sealed class TestPlayer : Player
        {
            public TestPlayer(
                IMovementManager movementManager,
                IInternalMessagePublisher messagePublisher,
                IEntityFactory entityFactory,
                IMatchingManager matchingManager,
                IMatchManager matchManager,
                ICurrencyManager currencyManager,
                IGuildManager guildManager,
                IResidenceManager residenceManager)
                : base(
                    movementManager,
                    messagePublisher,
                    entityFactory,
                    matchingManager,
                    matchManager,
                    currencyManager,
                    guildManager,
                    residenceManager)
            {
            }

            public bool HasStat(Stat stat)
            {
                return stats.ContainsKey(stat);
            }

            public float GetFloatStat(Stat stat)
            {
                return base.GetStatFloat(stat) ?? 0f;
            }

            public void SetFloatStat(Stat stat, float value)
            {
                SetStat(stat, value);
            }

            protected override float CalculateDefaultProperty(Property property)
            {
                return 0f;
            }

            protected override void OnPropertyUpdate(IPropertyValue propertyValue)
            {
            }

            protected override void OnStatUpdate(IStatValue statValue)
            {
            }
        }
    }
}
