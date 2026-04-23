using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Tests.Spell
{
    public class SpellStatusTests
    {
        [Fact]
        public void SpellStatus_HasAllSixStates()
        {
            Assert.True(Enum.IsDefined(typeof(SpellStatus), SpellStatus.Initiating));
            Assert.True(Enum.IsDefined(typeof(SpellStatus), SpellStatus.Casting));
            Assert.True(Enum.IsDefined(typeof(SpellStatus), SpellStatus.Executing));
            Assert.True(Enum.IsDefined(typeof(SpellStatus), SpellStatus.Finished));
            Assert.True(Enum.IsDefined(typeof(SpellStatus), SpellStatus.Finishing));
            Assert.True(Enum.IsDefined(typeof(SpellStatus), SpellStatus.Waiting));
        }

        [Fact]
        public void SpellStatus_OriginalValuesPreserved()
        {
            // Ensure original 4 values haven't shifted
            Assert.Equal(0, (int)SpellStatus.Initiating);
            Assert.Equal(1, (int)SpellStatus.Casting);
            Assert.Equal(2, (int)SpellStatus.Executing);
            Assert.Equal(3, (int)SpellStatus.Finished);
        }
    }
}
