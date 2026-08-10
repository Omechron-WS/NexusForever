using NexusForever.Game.Combat;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Combat
{
    public class DamageCalculatorTests
    {
        [Theory]
        [InlineData(100u, 0f, 150u)]
        [InlineData(100u, 0.25f, 175u)]
        public void CalculateCriticalDamage_AddsBaseSeverityAndRatingBonus(uint damage, float ratingBonus, uint expected)
        {
            uint result = DamageCalculator.CalculateCriticalDamage(damage, ratingBonus);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(100f, null, 25f)]
        [InlineData(100f, 0.4f, 40f)]
        public void CalculatePowerContribution_MultipliesRatingByFormulaOrFallback(float rating, float? coefficient, float expected)
        {
            float result = DamageCalculator.CalculatePowerContribution(rating, coefficient);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(DamageType.Physical, Property.DamageMitigationPctOffsetPhysical)]
        [InlineData(DamageType.Tech, Property.DamageMitigationPctOffsetTech)]
        [InlineData(DamageType.Magic, Property.DamageMitigationPctOffsetMagic)]
        public void GetDamageMitigationOffsetProperty_MapsDamageTypeToMatchingProperty(DamageType damageType, Property expected)
        {
            Property? result = DamageCalculator.GetDamageMitigationOffsetProperty(damageType);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(DamageType.Heal)]
        [InlineData(DamageType.HealShields)]
        [InlineData(DamageType.Fall)]
        [InlineData(DamageType.Suffocate)]
        public void GetDamageMitigationOffsetProperty_UnsupportedDamageType_ReturnsNull(DamageType damageType)
        {
            Property? result = DamageCalculator.GetDamageMitigationOffsetProperty(damageType);

            Assert.Null(result);
        }
    }
}
