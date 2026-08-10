using System;
using System.Linq;
using NexusForever.Game.Static.CSI;
using NexusForever.Game.Static.Housing;
using NexusForever.Game.Static.Loot;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Static
{
    public class EnumValidationTests
    {
        [Theory]
        [InlineData(typeof(LootEntityType))]
        [InlineData(typeof(LooterType))]
        [InlineData(typeof(LootConditionType))]
        [InlineData(typeof(LootItemType))]
        [InlineData(typeof(CastMethod))]
        [InlineData(typeof(TargetSelectionState))]
        [InlineData(typeof(SpellTargetMechanicType))]
        [InlineData(typeof(EffectForceSpellRemoveType))]
        [InlineData(typeof(EffectModifySpellCooldownType))]
        [InlineData(typeof(CSIType))]
        [InlineData(typeof(NeighborPermission))]
        public void Enum_HasNoDuplicateValues(Type enumType)
        {
            var values = Enum.GetValues(enumType).Cast<object>().ToList();
            var distinct = values.Select(v => Convert.ToInt64(v)).Distinct().ToList();

            Assert.Equal(distinct.Count, values.Count);
        }

        [Theory]
        [InlineData(typeof(TelegraphDamageFlag))]
        [InlineData(typeof(TelegraphTargetTypeFlags))]
        [InlineData(typeof(SpellTargetMechanicFlags))]
        [InlineData(typeof(UnlockedSpellSaveMask))]
        [InlineData(typeof(SpellEffectFlags))]
        public void FlagsEnum_HasFlagsAttribute(Type enumType)
        {
            Assert.True(enumType.IsDefined(typeof(FlagsAttribute), false),
                $"{enumType.Name} should have [Flags] attribute");
        }

        [Theory]
        [InlineData(typeof(TelegraphDamageFlag))]
        [InlineData(typeof(TelegraphTargetTypeFlags))]
        [InlineData(typeof(SpellTargetMechanicFlags))]
        [InlineData(typeof(UnlockedSpellSaveMask))]
        [InlineData(typeof(SpellEffectFlags))]
        public void FlagsEnum_NonZeroValuesArePowersOfTwo(Type enumType)
        {
            foreach (var value in Enum.GetValues(enumType))
            {
                long numericValue = Convert.ToInt64(value);
                if (numericValue == 0)
                    continue;

                bool isPowerOfTwo = (numericValue & (numericValue - 1)) == 0;
                Assert.True(isPowerOfTwo,
                    $"{enumType.Name}.{value} = {numericValue} is not a power of 2");
            }
        }

        [Fact]
        public void CastMethod_HasExpectedValues()
        {
            Assert.Equal(0, (int)CastMethod.Normal);
            Assert.Equal(1, (int)CastMethod.Channeled);
            Assert.Equal(5, (int)CastMethod.ClientSideInteraction);
            Assert.Equal(10, (int)CastMethod.Aura);
        }

        [Fact]
        public void CSIType_InteractionIsNegativeOne()
        {
            Assert.Equal(-1, (int)CSIType.Interaction);
        }

        [Fact]
        public void LootConditionType_HasExpectedRange()
        {
            Assert.Equal(0, (int)LootConditionType.None);
            Assert.Equal(8, (int)LootConditionType.QuestObjectiveActive);
        }

        [Fact]
        public void CommunityPrivacyLevel_FitsOneBitWireField()
        {
            Assert.Equal(0, (int)CommunityPrivacyLevel.Public);
            Assert.Equal(1, (int)CommunityPrivacyLevel.Private);
        }
    }
}
