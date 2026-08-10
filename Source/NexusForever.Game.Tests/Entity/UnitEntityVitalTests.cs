using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    [Collection(VitalServiceProviderCollection.Name)]
    public sealed class UnitEntityVitalTests : IDisposable
    {
        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public UnitEntityVitalTests()
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

        public static TheoryData<Vital, Property> BoundedVitals => new()
        {
            { Vital.Health, Property.BaseHealth },
            { Vital.ShieldCapacity, Property.ShieldCapacityMax },
            { Vital.Resource0, Property.ResourceMax0 },
            { Vital.Focus, Property.BaseFocusPool },
            { Vital.Resource7, Property.ResourceMax7 },
            { Vital.Resource1, Property.ResourceMax1 },
            { Vital.KineticCell, Property.ResourceMax1 },
            { Vital.StalkerB, Property.ResourceMax1 },
            { Vital.MedicCore, Property.ResourceMax1 },
            { Vital.Volatility, Property.ResourceMax1 },
            { Vital.Resource2, Property.ResourceMax2 },
            { Vital.Resource3, Property.ResourceMax3 },
            { Vital.StalkerA, Property.ResourceMax3 },
            { Vital.Resource4, Property.ResourceMax4 },
            { Vital.SpellSurge, Property.ResourceMax4 },
            { Vital.Resource5, Property.ResourceMax5 },
            { Vital.Resource6, Property.ResourceMax6 }
        };

        public static TheoryData<Vital> UnsupportedVitals => new()
        {
            Vital.Invalid,
            Vital.Breath,
            Vital.StalkerC,
            Vital.Absorption,
            Vital.PublicResource0,
            Vital.PublicResource1,
            Vital.PublicResource2,
            Vital.Resource8,
            Vital.Resource9,
            Vital.Resource10,
            Vital.HealingAbsorption,
            (Vital)0x11,
            (Vital)0x19,
            (Vital)0xFF
        };

        [Theory]
        [MemberData(nameof(BoundedVitals))]
        public void BoundedVital_UsesConfiguredMaximumAndClamps(Vital vital, Property maximumProperty)
        {
            TestUnitEntity entity = CreateEntity();
            entity.SetBaseProperty(maximumProperty, 100f);
            if (vital == Vital.Health)
                entity.SetHealth(1u);

            Assert.True(entity.TryGetVitalMaximum(vital, out float maximum));
            Assert.Equal(100f, maximum);
            Assert.True(entity.TryModifyVital(vital, 125f));
            Assert.True(entity.TryGetVitalValue(vital, out float value));
            Assert.Equal(100f, value);
            Assert.True(entity.TryModifyVital(vital, -150f));
            Assert.True(entity.TryGetVitalValue(vital, out value));
            Assert.Equal(0f, value);
        }

        [Theory]
        [InlineData(Vital.KineticCell)]
        [InlineData(Vital.StalkerB)]
        [InlineData(Vital.MedicCore)]
        [InlineData(Vital.Volatility)]
        public void Resource1Alias_SharesCanonicalStorage(Vital alias)
        {
            TestUnitEntity entity = CreateEntity();
            entity.SetBaseProperty(Property.ResourceMax1, 100f);

            Assert.True(entity.TryModifyVital(alias, 25f));
            Assert.True(entity.TryGetVitalValue(Vital.Resource1, out float canonicalValue));
            Assert.Equal(25f, canonicalValue);

            Assert.True(entity.TryModifyVital(Vital.Resource1, 10f));
            Assert.True(entity.TryGetVitalValue(alias, out float aliasValue));
            Assert.Equal(35f, aliasValue);
        }

        [Theory]
        [InlineData(Vital.StalkerA, Vital.Resource3, Property.ResourceMax3)]
        [InlineData(Vital.SpellSurge, Vital.Resource4, Property.ResourceMax4)]
        public void ClassResourceAlias_SharesCanonicalStorage(Vital alias, Vital canonical, Property maximumProperty)
        {
            TestUnitEntity entity = CreateEntity();
            entity.SetBaseProperty(maximumProperty, 100f);

            Assert.True(entity.TryModifyVital(alias, 25f));
            Assert.True(entity.TryGetVitalValue(canonical, out float canonicalValue));
            Assert.Equal(25f, canonicalValue);
        }

        [Fact]
        public void InterruptArmor_HasNoFabricatedMaximumAndClampsOnlyAtZero()
        {
            TestUnitEntity entity = CreateEntity();

            Assert.False(entity.TryGetVitalMaximum(Vital.InterruptArmor, out float maximum));
            Assert.Equal(0f, maximum);
            Assert.True(entity.TryGetVitalValue(Vital.InterruptArmor, out float initial));
            Assert.Equal(1f, initial);

            Assert.True(entity.TryModifyVital(Vital.InterruptArmor, 9f));
            Assert.True(entity.TryGetVitalValue(Vital.InterruptArmor, out float increased));
            Assert.Equal(10f, increased);

            Assert.True(entity.TryModifyVital(Vital.InterruptArmor, -20f));
            Assert.True(entity.TryGetVitalValue(Vital.InterruptArmor, out float clamped));
            Assert.Equal(0f, clamped);
        }

        [Fact]
        public void IntegerVital_TruncatesResultTowardsZero()
        {
            TestUnitEntity entity = CreateEntity();
            entity.MaxShieldCapacity = 100u;

            Assert.True(entity.TryModifyVital(Vital.ShieldCapacity, 10.9f));
            Assert.Equal(10u, entity.Shield);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void NonFiniteDelta_FailsWithoutMutation(float delta)
        {
            TestUnitEntity entity = CreateEntity();
            entity.SetBaseProperty(Property.ResourceMax0, 100f);
            Assert.True(entity.TryModifyVital(Vital.Resource0, 25f));

            Assert.False(entity.TryModifyVital(Vital.Resource0, delta));
            Assert.True(entity.TryGetVitalValue(Vital.Resource0, out float value));
            Assert.Equal(25f, value);
        }

        [Fact]
        public void IntegerConversionOverflow_FailsWithoutMutation()
        {
            TestUnitEntity entity = CreateEntity();

            Assert.False(entity.TryModifyVital(Vital.InterruptArmor, float.MaxValue));
            Assert.True(entity.TryGetVitalValue(Vital.InterruptArmor, out float value));
            Assert.Equal(1f, value);
        }

        [Theory]
        [MemberData(nameof(UnsupportedVitals))]
        public void UnsupportedVital_FailsClosedWithoutMutation(Vital unsupported)
        {
            TestUnitEntity entity = CreateEntity();
            entity.SetBaseProperty(Property.ResourceMax0, 100f);
            Assert.True(entity.TryModifyVital(Vital.Resource0, 25f));

            Assert.False(entity.TryGetVitalValue(unsupported, out float value));
            Assert.Equal(0f, value);
            Assert.False(entity.TryGetVitalMaximum(unsupported, out float maximum));
            Assert.Equal(0f, maximum);
            Assert.False(entity.TryModifyVital(unsupported, 50f));
            Assert.True(entity.TryGetVitalValue(Vital.Resource0, out float unchanged));
            Assert.Equal(25f, unchanged);
        }

        [Fact]
        public void HealthDelta_UsesHealAndDamageSemanticsAndReportsDeathOnce()
        {
            TestUnitEntity entity = CreateEntity();
            var source = new Mock<IUnitEntity>();
            entity.MaxHealth = 100u;
            entity.SetHealth(50u);

            Assert.True(entity.TryModifyVital(Vital.Health, 25f, source.Object));
            Assert.Equal(75u, entity.Health);
            Assert.Equal(DamageType.Heal, entity.LastDamageType);
            Assert.Same(source.Object, entity.LastSource);

            Assert.True(entity.TryModifyVital(Vital.Health, -100f, source.Object));
            Assert.Equal(0u, entity.Health);
            Assert.Equal(DamageType.Physical, entity.LastDamageType);
            Assert.Equal(1, entity.DeathCount);

            Assert.True(entity.TryModifyVital(Vital.Health, -1f, source.Object));
            Assert.Equal(1, entity.DeathCount);
        }

        [Fact]
        public void DeadHealth_PositiveDeltaFailsWithoutResurrection()
        {
            TestUnitEntity entity = CreateEntity();
            entity.MaxHealth = 100u;
            entity.SetHealth(50u);
            Assert.True(entity.TryModifyVital(Vital.Health, -50f));
            Assert.Equal(1, entity.DeathCount);

            Assert.False(entity.TryModifyVital(Vital.Health, 50f));
            Assert.Equal(0u, entity.Health);
            Assert.False(entity.IsAlive);
            Assert.Equal(1, entity.DeathCount);
        }

        private static TestUnitEntity CreateEntity()
        {
            var movementManager = new Mock<IMovementManager>();
            return new TestUnitEntity(movementManager.Object);
        }

        private sealed class TestUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public int DeathCount { get; private set; }
            public DamageType? LastDamageType { get; private set; }
            public IUnitEntity LastSource { get; private set; }

            public TestUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void SetHealth(uint health)
            {
                Health = health;
            }

            public override void ModifyHealth(uint amount, DamageType type, IUnitEntity source)
            {
                LastDamageType = type;
                LastSource = source;
                base.ModifyHealth(amount, type, source);
            }

            protected override float CalculateDefaultProperty(Property property)
            {
                return 0f;
            }

            protected override void OnDeath()
            {
                DeathCount++;
            }

            protected override IEntityModel BuildEntityModel()
            {
                return new NonPlayerEntityModel();
            }
        }
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class VitalServiceProviderCollection
    {
        public const string Name = "Vital service provider";
    }
}
