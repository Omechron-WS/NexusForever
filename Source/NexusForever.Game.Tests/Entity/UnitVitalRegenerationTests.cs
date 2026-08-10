using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    [Collection(VitalServiceProviderCollection.Name)]
    public sealed class UnitVitalRegenerationTests : IDisposable
    {
        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public UnitVitalRegenerationTests()
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

        [Fact]
        public void Update_RegeneratesHealthAndShieldOnHalfSecondTick()
        {
            TestUnitEntity entity = CreateEntity(1u);
            entity.MaxHealth = 100u;
            entity.SetHealth(50u);
            entity.MaxShieldCapacity = 100u;
            entity.Shield = 50u;
            entity.SetBaseProperty(Property.ShieldRegenPct, 0.1f);

            entity.Update(0.25d);

            Assert.Equal(50u, entity.Health);
            Assert.Equal(50u, entity.Shield);

            entity.Update(0.25d);

            Assert.Equal(52u, entity.Health);
            Assert.Equal(55u, entity.Shield);
        }

        [Fact]
        public void Update_ProcessesDeterministicMultiTickCatchUp()
        {
            TestUnitEntity entity = CreateEntity(1u);
            entity.MaxHealth = 1000u;
            entity.SetHealth(100u);

            entity.Update(1.5d);

            Assert.Equal(160u, entity.Health);
        }

        [Fact]
        public void Update_PreservesFractionalHealthRegenerationAcrossTicks()
        {
            TestUnitEntity entity = CreateEntity(1u);
            entity.MaxHealth = 25u;
            entity.SetHealth(10u);

            entity.Update(0.5d);
            Assert.Equal(10u, entity.Health);

            entity.Update(0.5d);
            Assert.Equal(11u, entity.Health);
        }

        [Fact]
        public void Update_DoesNotRegenerateInCombat()
        {
            TestUnitEntity entity = CreateEntity(1u);
            TestUnitEntity target = CreateEntity(2u);
            entity.MaxHealth = 100u;
            entity.SetHealth(50u);
            target.MaxHealth = 100u;
            target.SetHealth(100u);
            entity.ThreatManager.UpdateThreat(target, 10);

            entity.Update(1d);

            Assert.True(entity.InCombat);
            Assert.Equal(50u, entity.Health);
        }

        [Fact]
        public void Update_DoesNotRegenerateOrReviveDeadUnit()
        {
            TestUnitEntity entity = CreateEntity(1u);
            entity.MaxHealth = 100u;
            entity.SetHealth(0u);

            entity.Update(10d);

            Assert.False(entity.IsAlive);
            Assert.Equal(0u, entity.Health);
        }

        [Fact]
        public void Update_ExtremeElapsedTimeUsesBoundedCatchUp()
        {
            TestUnitEntity entity = CreateEntity(1u);
            entity.MaxHealth = 1000u;
            entity.SetHealth(100u);

            entity.Update(1000d);

            Assert.Equal(500u, entity.Health);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1d)]
        [InlineData(0d)]
        public void Update_InvalidElapsedTimeIsIgnored(double elapsed)
        {
            TestUnitEntity entity = CreateEntity(1u);
            entity.MaxHealth = 100u;
            entity.SetHealth(50u);

            entity.Update(elapsed);

            Assert.Equal(50u, entity.Health);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(-0.1f)]
        [InlineData(0f)]
        public void Update_InvalidShieldRegenerationRateIsIgnored(float regenerationRate)
        {
            TestUnitEntity entity = CreateEntity(1u);
            entity.MaxHealth = 1u;
            entity.SetHealth(1u);
            entity.MaxShieldCapacity = 100u;
            entity.Shield = 50u;
            entity.SetBaseProperty(Property.ShieldRegenPct, regenerationRate);

            entity.Update(0.5d);

            Assert.Equal(50u, entity.Shield);
        }

        [Fact]
        public void Update_MaximumReductionDoesNotUnderflowOrRegenerateAboveMaximum()
        {
            TestUnitEntity entity = CreateEntity(1u);
            entity.MaxHealth = 100u;
            entity.SetHealth(90u);
            entity.MaxShieldCapacity = 100u;
            entity.Shield = 90u;
            entity.SetBaseProperty(Property.ShieldRegenPct, 1f);

            entity.MaxHealth = 50u;
            entity.MaxShieldCapacity = 50u;
            entity.Update(0.5d);

            Assert.Equal(90u, entity.Health);
            Assert.Equal(90u, entity.Shield);
        }

        private static TestUnitEntity CreateEntity(uint guid)
        {
            var entity = new TestUnitEntity(new Mock<IMovementManager>().Object);
            entity.SetGuid(guid);
            return entity;
        }

        private sealed class TestUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public TestUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void SetGuid(uint guid)
            {
                Guid = guid;
            }

            public void SetHealth(uint health)
            {
                Health = health;
            }

            protected override float CalculateDefaultProperty(Property property)
            {
                return 0f;
            }

            protected override void OnCombatStateChange(bool inCombat)
            {
            }

            protected override IEntityModel BuildEntityModel()
            {
                return new NonPlayerEntityModel();
            }
        }
    }
}
