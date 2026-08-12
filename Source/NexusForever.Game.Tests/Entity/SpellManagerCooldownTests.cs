using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using Moq;

namespace NexusForever.Game.Tests.Entity
{
    public sealed class SpellManagerCooldownTests
    {
        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_AtomicallyZerosTrackedRegisteredTiersWithoutPacket()
        {
            var packets = new List<IWritable>();
            SpellManager manager = CreateManager(
                [Tier(34718u), Tier(48940u), Tier(48941u)],
                packets);
            manager.SetSpellCooldown(34718u, 10d);
            manager.SetSpellCooldown(48940u, 4d);
            manager.SetSpellCooldown(999999u, 7d);
            packets.Clear();

            bool reset = manager.TryResetSpellCooldownsByBaseSpell(20684u);

            Assert.True(reset);
            Assert.Equal(0d, manager.GetSpellCooldown(34718u));
            Assert.Equal(0d, manager.GetSpellCooldown(48940u));
            Assert.Equal(7d, manager.GetSpellCooldown(999999u));
            Assert.Empty(packets);
        }

        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_UnrelatedInvalidTrackedKeyDoesNotBlock()
        {
            SpellManager manager = CreateManager([Tier(34718u)]);
            manager.SetSpellCooldown(34718u, 10d);
            manager.SetSpellCooldown(uint.MaxValue, 5d);

            Assert.True(manager.TryResetSpellCooldownsByBaseSpell(20684u));
            Assert.Equal(0d, manager.GetSpellCooldown(34718u));
            Assert.Equal(5d, manager.GetSpellCooldown(uint.MaxValue));
        }

        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_ExistingBaseWithoutTrackedTierIsSuccessfulNoOp()
        {
            SpellManager manager = CreateManager([Tier(34718u)]);
            manager.SetSpellCooldown(999999u, 5d);

            Assert.True(manager.TryResetSpellCooldownsByBaseSpell(20684u));
            Assert.Equal(5d, manager.GetSpellCooldown(999999u));
        }

        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_UnknownBaseFailsWithoutMutation()
        {
            SpellManager manager = CreateManager([Tier(34718u)]);
            manager.SetSpellCooldown(34718u, 10d);

            Assert.False(manager.TryResetSpellCooldownsByBaseSpell(20685u));
            Assert.Equal(10d, manager.GetSpellCooldown(34718u));
        }

        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_LazyMaterializationFailureIsAtomic()
        {
            SpellManager manager = CreateManager(_ => ThrowingEntries());
            manager.SetSpellCooldown(34718u, 10d);

            Assert.False(manager.TryResetSpellCooldownsByBaseSpell(20684u));
            Assert.Equal(10d, manager.GetSpellCooldown(34718u));
        }

        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_BaseLookupExceptionFailsWithoutEnumerationOrMutation()
        {
            int enumerationReads = 0;
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.IsLoading).Returns(true);
            var manager = new SpellManager(
                player.Object,
                _ => throw new InvalidOperationException("Test base lookup failure."),
                _ =>
                {
                    enumerationReads++;
                    return [Tier(34718u)];
                });
            manager.SetSpellCooldown(34718u, 10d);

            Assert.False(manager.TryResetSpellCooldownsByBaseSpell(20684u));
            Assert.Equal(10d, manager.GetSpellCooldown(34718u));
            Assert.Equal(0, enumerationReads);
        }

        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_EmptyDuplicateOrZeroRegisteredSetsFailAtomically()
        {
            SpellManager empty = CreateManager([]);
            empty.SetSpellCooldown(34718u, 10d);
            Assert.False(empty.TryResetSpellCooldownsByBaseSpell(20684u));
            Assert.Equal(10d, empty.GetSpellCooldown(34718u));

            Spell4Entry tier = Tier(34718u);
            SpellManager duplicate = CreateManager([tier, tier]);
            duplicate.SetSpellCooldown(34718u, 10d);
            Assert.False(duplicate.TryResetSpellCooldownsByBaseSpell(20684u));
            Assert.Equal(10d, duplicate.GetSpellCooldown(34718u));

            SpellManager zero = CreateManager([Tier(0u)]);
            zero.SetSpellCooldown(0u, 10d);
            Assert.False(zero.TryResetSpellCooldownsByBaseSpell(20684u));
            Assert.Equal(10d, zero.GetSpellCooldown(0u));
        }

        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_NullOrMismatchedTierSetFailsAtomically()
        {
            foreach (Func<uint, IEnumerable<Spell4Entry>> entries in new Func<uint, IEnumerable<Spell4Entry>>[]
            {
                _ => null,
                _ => [Tier(34718u), new Spell4Entry { Id = 1u, Spell4BaseIdBaseSpell = 20685u }],
                _ => [Tier(34718u), null]
            })
            {
                SpellManager manager = CreateManager(entries);
                manager.SetSpellCooldown(34718u, 10d);

                Assert.False(manager.TryResetSpellCooldownsByBaseSpell(20684u));
                Assert.Equal(10d, manager.GetSpellCooldown(34718u));
            }
        }

        [Fact]
        public void TryResetSpellCooldownsByBaseSpell_MismatchedBaseEntryIdentityFailsBeforeTierRead()
        {
            int tierReads = 0;
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.IsLoading).Returns(true);
            var manager = new SpellManager(
                player.Object,
                _ => new Spell4BaseEntry { Id = 20685u },
                _ =>
                {
                    tierReads++;
                    return [Tier(34718u)];
                });
            manager.SetSpellCooldown(34718u, 10d);

            Assert.False(manager.TryResetSpellCooldownsByBaseSpell(20684u));
            Assert.Equal(10d, manager.GetSpellCooldown(34718u));
            Assert.Equal(0, tierReads);
        }

        [Fact]
        public void ExistingSetAndResetAllCooldownApisContinueToNotify()
        {
            var packets = new List<IWritable>();
            SpellManager manager = CreateManager([Tier(34718u), Tier(48940u)], packets);

            manager.SetSpellCooldown(34718u, 10d);
            manager.SetSpellCooldown(48940u, 5d);
            manager.ResetAllSpellCooldowns();

            Assert.Equal(
                new uint[] { 10000u, 5000u, 0u, 0u },
                packets.Select(packet => Assert.IsType<NexusForever.Network.World.Message.Model.ServerCooldown>(packet)
                    .Cooldown.TimeRemaining).ToArray());
            Assert.Equal(0d, manager.GetSpellCooldown(34718u));
            Assert.Equal(0d, manager.GetSpellCooldown(48940u));
        }

        private static SpellManager CreateManager(
            Spell4Entry[] entries,
            List<IWritable> packets = null)
        {
            return CreateManager(_ => entries, packets);
        }

        private static SpellManager CreateManager(
            Func<uint, IEnumerable<Spell4Entry>> getEntries,
            List<IWritable> packets = null)
        {
            var session = new Mock<IGameSession>();
            if (packets != null)
            {
                session.Setup(value => value.EnqueueMessageEncrypted(It.IsAny<IWritable>()))
                    .Callback<IWritable>(packets.Add);
            }

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.IsLoading).Returns(packets == null);
            player.SetupGet(value => value.Session).Returns(session.Object);
            return new SpellManager(
                player.Object,
                spell4BaseId => spell4BaseId == 20684u
                    ? new Spell4BaseEntry { Id = spell4BaseId }
                    : null,
                getEntries);
        }

        private static Spell4Entry Tier(uint spell4Id)
        {
            return new Spell4Entry
            {
                Id = spell4Id,
                Spell4BaseIdBaseSpell = 20684u
            };
        }

        private static IEnumerable<Spell4Entry> ThrowingEntries()
        {
            yield return Tier(34718u);
            throw new InvalidOperationException("Test materialization failure.");
        }
    }
}
