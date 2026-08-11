using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    public sealed class SpellManagerGlobalCooldownTests
    {
        [Fact]
        public void TypedLanes_StoreReplaceAndExpireIndependently()
        {
            var manager = new SpellManager(Mock.Of<IPlayer>());

            Assert.Equal(0d, manager.GetGlobalSpellCooldown(99u));

            manager.SetGlobalSpellCooldown(0u, 2d);
            manager.SetGlobalSpellCooldown(3u, 1.5d);
            manager.SetGlobalSpellCooldown(99u, 0.25d);
            manager.Update(0.5d);

            Assert.Equal(1.5d, manager.GetGlobalSpellCooldown(0u));
            Assert.Equal(1d, manager.GetGlobalSpellCooldown(3u));
            Assert.Equal(0d, manager.GetGlobalSpellCooldown(99u));

            manager.SetGlobalSpellCooldown(3u, 0.75d);
            manager.SetGlobalSpellCooldown(0u, 0d);

            Assert.Equal(0d, manager.GetGlobalSpellCooldown(0u));
            Assert.Equal(0.75d, manager.GetGlobalSpellCooldown(3u));
        }

        [Fact]
        public void LegacyAccessors_MapOnlyToLaneZero()
        {
            var manager = new SpellManager(Mock.Of<IPlayer>());
            manager.SetGlobalSpellCooldown(3u, 1.5d);

            manager.SetGlobalSpellCooldown(2d);

            Assert.Equal(2d, manager.GetGlobalSpellCooldown());
            Assert.Equal(2d, manager.GetGlobalSpellCooldown(0u));
            Assert.Equal(1.5d, manager.GetGlobalSpellCooldown(3u));

            manager.SetGlobalSpellCooldown(0d);

            Assert.Equal(0d, manager.GetGlobalSpellCooldown(0u));
            Assert.Equal(1.5d, manager.GetGlobalSpellCooldown(3u));
        }

        [Theory]
        [InlineData(-1d)]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidCooldown_FailsBeforeTypedOrLegacyLaneMutation(double cooldown)
        {
            var manager = new SpellManager(Mock.Of<IPlayer>());
            manager.SetGlobalSpellCooldown(0u, 2d);
            manager.SetGlobalSpellCooldown(3u, 1.5d);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                manager.SetGlobalSpellCooldown(3u, cooldown));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                manager.SetGlobalSpellCooldown(cooldown));

            Assert.Equal(2d, manager.GetGlobalSpellCooldown(0u));
            Assert.Equal(1.5d, manager.GetGlobalSpellCooldown(3u));
        }

        [Fact]
        public void MinimalConstructor_RejectsNullOwner()
        {
            Assert.Throws<ArgumentNullException>(() => new SpellManager(null));
        }
    }
}
