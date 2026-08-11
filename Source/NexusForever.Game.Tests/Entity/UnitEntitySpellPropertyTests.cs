using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Combat;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Shared;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    [Collection(VitalServiceProviderCollection.Name)]
    public sealed class UnitEntitySpellPropertyTests : IDisposable
    {
        private const uint SourceSpell4Id = 100u;

        private readonly IServiceProvider previousProvider;
        private readonly ServiceProvider serviceProvider;

        public UnitEntitySpellPropertyTests()
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

        [Theory]
        [InlineData(2u, 1u)]
        [InlineData(1u, 2u)]
        public void SameSpellOwners_RemoveIndependentlyWithoutStacking(
            uint secondCastingId,
            uint secondEffectId)
        {
            TestUnitEntity entity = CreateEntity();
            SpellPropertyModifier first = CreateModifier(
                new SpellEffectIdentity(1u, SourceSpell4Id, 1u),
                5f);
            SpellPropertyModifier second = CreateModifier(
                new SpellEffectIdentity(secondCastingId, SourceSpell4Id, secondEffectId),
                7f);

            Assert.True(entity.AddSpellModifierProperty(first));
            Assert.Equal(15f, entity.GetPropertyValue(Property.Strength));

            Assert.True(entity.AddSpellModifierProperty(second));
            Assert.Equal(17f, entity.GetPropertyValue(Property.Strength));

            Assert.True(entity.RemoveSpellModifierProperty(Property.Strength, second.Identity));
            Assert.Equal(15f, entity.GetPropertyValue(Property.Strength));

            Assert.True(entity.RemoveSpellModifierProperty(Property.Strength, first.Identity));
            Assert.Equal(10f, entity.GetPropertyValue(Property.Strength));
        }

        [Fact]
        public void RemovingOlderOwner_LeavesNewestOwnerActive()
        {
            TestUnitEntity entity = CreateEntity();
            SpellPropertyModifier first = CreateModifier(
                new SpellEffectIdentity(1u, SourceSpell4Id, 1u),
                5f);
            SpellPropertyModifier second = CreateModifier(
                new SpellEffectIdentity(2u, SourceSpell4Id, 1u),
                7f);
            entity.AddSpellModifierProperty(first);
            entity.AddSpellModifierProperty(second);

            Assert.True(entity.RemoveSpellModifierProperty(Property.Strength, first.Identity));

            Assert.Equal(17f, entity.GetPropertyValue(Property.Strength));
            Assert.True(entity.RemoveSpellModifierProperty(Property.Strength, second.Identity));
            Assert.Equal(10f, entity.GetPropertyValue(Property.Strength));
        }

        [Fact]
        public void ReapplyingIdentity_RefreshesOneOwnedModifier()
        {
            TestUnitEntity entity = CreateEntity();
            var identity = new SpellEffectIdentity(1u, SourceSpell4Id, 1u);

            Assert.True(entity.AddSpellModifierProperty(CreateModifier(identity, 5f)));
            Assert.True(entity.AddSpellModifierProperty(CreateModifier(identity, 8f)));
            Assert.Equal(18f, entity.GetPropertyValue(Property.Strength));

            Assert.True(entity.RemoveSpellModifierProperty(Property.Strength, identity));
            Assert.False(entity.RemoveSpellModifierProperty(Property.Strength, identity));
            Assert.Equal(10f, entity.GetPropertyValue(Property.Strength));
        }

        [Fact]
        public void DifferentSourceSpells_ContinueToCombine()
        {
            TestUnitEntity entity = CreateEntity();
            entity.AddSpellModifierProperty(CreateModifier(
                new SpellEffectIdentity(1u, SourceSpell4Id, 1u),
                5f));
            entity.AddSpellModifierProperty(CreateModifier(
                new SpellEffectIdentity(2u, 200u, 2u),
                7f));

            Assert.Equal(22f, entity.GetPropertyValue(Property.Strength));
        }

        [Fact]
        public void FailedCalculation_RollsBackOwnedModifier()
        {
            TestUnitEntity entity = CreateEntity();
            var alteration = new Mock<IPropertyModifier>();
            alteration.SetupGet(value => value.Property).Returns(Property.Strength);
            alteration.SetupGet(value => value.ModType).Returns(ModType.FlatValue);
            alteration.Setup(value => value.GetValue(It.IsAny<uint>()))
                .Throws(new InvalidOperationException("Test modifier failure."));
            var modifier = new Mock<ISpellPropertyModifier>();
            var identity = new SpellEffectIdentity(1u, SourceSpell4Id, 1u);
            modifier.SetupGet(value => value.Identity).Returns(identity);
            modifier.SetupGet(value => value.Property).Returns(Property.Strength);
            modifier.SetupGet(value => value.Priority).Returns(1u);
            modifier.SetupGet(value => value.Alterations).Returns([alteration.Object]);

            Assert.Throws<InvalidOperationException>(() =>
                entity.AddSpellModifierProperty(modifier.Object));

            Assert.Equal(10f, entity.GetPropertyValue(Property.Strength));
            Assert.False(entity.RemoveSpellModifierProperty(Property.Strength, identity));
        }

        [Fact]
        public void FailedRemovalCalculation_RestoresExactOwnerForSuccessfulRetry()
        {
            TestUnitEntity entity = CreateEntity();
            var identity = new SpellEffectIdentity(1u, SourceSpell4Id, 1u);
            entity.AddSpellModifierProperty(CreateModifier(identity, 5f));
            var expected = new InvalidOperationException("Test removal calculation failure.");
            entity.NextPropertyCalculationException = expected;

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                entity.RemoveSpellModifierProperty(Property.Strength, identity));

            Assert.Same(expected, actual);
            Assert.Equal(15f, entity.GetPropertyValue(Property.Strength));
            Assert.True(entity.RemoveSpellModifierProperty(Property.Strength, identity));
            Assert.Equal(10f, entity.GetPropertyValue(Property.Strength));
        }

        [Fact]
        public void RemoveProc_CustomEqualityRemovesAndCancelsExactReference()
        {
            TestUnitEntity entity = CreateEntity();
            var first = new EqualityCollidingProcInfo(entity, 1u);
            var second = new EqualityCollidingProcInfo(entity, 2u);
            var duplicateFirstEffect = new EqualityCollidingProcInfo(entity, 1u);
            Assert.True(entity.ApplyProc(first));
            Assert.True(entity.ApplyProc(second));

            Assert.True(entity.RemoveProc(second));

            Assert.Equal(0, first.CancelCount);
            Assert.Equal(1, second.CancelCount);
            Assert.False(entity.ApplyProc(duplicateFirstEffect));
            Assert.True(entity.RemoveProc(first));
            Assert.Equal(1, first.CancelCount);
        }

        [Fact]
        public void Death_ClearsModifiersAndRejectsNewApplication()
        {
            TestUnitEntity entity = CreateEntity();
            SpellPropertyModifier modifier = CreateModifier(
                new SpellEffectIdentity(1u, SourceSpell4Id, 1u),
                5f);
            entity.AddSpellModifierProperty(modifier);

            entity.Kill();

            Assert.Equal(10f, entity.GetPropertyValue(Property.Strength));
            Assert.False(entity.AddSpellModifierProperty(modifier));
        }

        [Fact]
        public void Dispose_ClearsModifiers()
        {
            TestUnitEntity entity = CreateEntity();
            entity.AddSpellModifierProperty(CreateModifier(
                new SpellEffectIdentity(1u, SourceSpell4Id, 1u),
                5f));

            entity.Dispose();

            Assert.Equal(10f, entity.GetPropertyValue(Property.Strength));
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
            serviceProvider.Dispose();
        }

        private static TestUnitEntity CreateEntity()
        {
            var entity = new TestUnitEntity(Mock.Of<IMovementManager>());
            entity.MaxHealth = 100u;
            entity.SetHealth(100u);
            entity.SetBaseProperty(Property.Strength, 10f);
            return entity;
        }

        private static SpellPropertyModifier CreateModifier(
            SpellEffectIdentity identity,
            float flatValue)
        {
            return new SpellPropertyModifier(
                identity,
                Property.Strength,
                1u,
                0f,
                flatValue,
                0f);
        }

        private sealed class TestUnitEntity : UnitEntity
        {
            public override EntityType Type => EntityType.NonPlayer;

            public Exception NextPropertyCalculationException { get; set; }

            public TestUnitEntity(IMovementManager movementManager)
                : base(movementManager)
            {
            }

            public void SetHealth(uint health)
            {
                Health = health;
            }

            public void Kill()
            {
                ModifyHealth(Health, DamageType.Physical, null);
            }

            protected override void PublishDeathState()
            {
            }

            protected override void GenerateRewards()
            {
            }

            protected override void ClearThreatsAfterDeath()
            {
            }

            protected override float CalculateDefaultProperty(Property property)
            {
                return 0f;
            }

            protected override void CalculatePropertyValue(IPropertyValue propertyValue)
            {
                if (NextPropertyCalculationException != null)
                {
                    Exception exception = NextPropertyCalculationException;
                    NextPropertyCalculationException = null;
                    throw exception;
                }

                base.CalculatePropertyValue(propertyValue);
            }

            protected override IEntityModel BuildEntityModel()
            {
                return new NonPlayerEntityModel();
            }
        }

        private sealed class EqualityCollidingProcInfo : IProcInfo
        {
            public IUnitEntity Owner { get; }
            public uint EffectId { get; }
            public uint ApplicatorSpell4Id => 0u;
            public ProcType Type => ProcType.OnHit;
            public uint TriggerSpell4Id => 0u;
            public float Chance => 1f;
            public bool CanTrigger => true;

            public int CancelCount { get; private set; }

            public EqualityCollidingProcInfo(IUnitEntity owner, uint effectId)
            {
                Owner = owner;
                EffectId = effectId;
            }

            public bool Trigger(IUnitEntity primaryTarget = null)
            {
                return true;
            }

            public void Cancel()
            {
                CancelCount++;
            }

            public void Update(double lastTick)
            {
            }

            public override bool Equals(object obj)
            {
                return obj is EqualityCollidingProcInfo;
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}
