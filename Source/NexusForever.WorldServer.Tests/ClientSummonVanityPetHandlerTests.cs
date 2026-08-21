using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Pet;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientSummonVanityPetHandlerTests
    {
        [Fact]
        public void UnknownBase_IsRejectedBeforeMetadataOrCast()
        {
            var fixture = new Fixture();
            fixture.SpellManager
                .Setup(value => value.GetSpell(Fixture.BaseId))
                .Returns((ICharacterSpell)null);

            fixture.AssertRejected();

            fixture.CharacterSpell.VerifyGet(value => value.Owner, Times.Never);
            fixture.CharacterSpell.VerifyGet(value => value.BaseInfo, Times.Never);
            fixture.CharacterSpell.VerifyGet(value => value.SpellInfo, Times.Never);
            fixture.Player.Verify(value => value.CastSpell(It.IsAny<ISpellParameters>()), Times.Never);
            fixture.SpellManager.Verify(value => value.GetSpellTier(It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void ForeignOwner_IsRejectedBeforeSpellMetadataOrCast()
        {
            var fixture = new Fixture();
            var foreignOwner = new Mock<IPlayer>(MockBehavior.Strict);
            fixture.CharacterSpell.SetupGet(value => value.Owner).Returns(foreignOwner.Object);

            fixture.AssertRejected();

            fixture.CharacterSpell.VerifyGet(value => value.BaseInfo, Times.Never);
            fixture.CharacterSpell.VerifyGet(value => value.SpellInfo, Times.Never);
            fixture.Player.Verify(value => value.CastSpell(It.IsAny<ISpellParameters>()), Times.Never);
        }

        [Fact]
        public void MismatchedBase_IsRejectedBeforeSpellInfoOrCast()
        {
            var fixture = new Fixture();
            fixture.BaseEntry.Id = Fixture.BaseId + 1u;

            fixture.AssertRejected();

            fixture.CharacterSpell.VerifyGet(value => value.SpellInfo, Times.Never);
            fixture.Player.Verify(value => value.CastSpell(It.IsAny<ISpellParameters>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void MismatchedSpellInfoLineage_IsRejectedBeforeEffectsOrCast(int shape)
        {
            var fixture = new Fixture();
            switch (shape)
            {
                case 0:
                    fixture.SpellInfo
                        .SetupGet(value => value.BaseInfo)
                        .Returns(Mock.Of<ISpellBaseInfo>());
                    break;
                case 1:
                    fixture.SpellEntry.Id = 0u;
                    break;
                case 2:
                    fixture.SpellEntry.Spell4BaseIdBaseSpell = Fixture.BaseId + 1u;
                    break;
            }

            fixture.AssertRejected();

            fixture.SpellInfo.VerifyGet(value => value.Effects, Times.Never);
            fixture.Player.Verify(value => value.CastSpell(It.IsAny<ISpellParameters>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void InvalidVanityEffect_IsRejectedBeforeCast(int shape)
        {
            var fixture = new Fixture();
            switch (shape)
            {
                case 0:
                    fixture.VanityEffect.EffectType = SpellEffectType.AchievementAdvance;
                    break;
                case 1:
                    fixture.VanityEffect.SpellId = Fixture.SpellId + 1u;
                    break;
                case 2:
                    fixture.VanityEffect.DataBits00 = 0u;
                    break;
            }

            fixture.AssertRejected();

            fixture.Player.Verify(value => value.CastSpell(It.IsAny<ISpellParameters>()), Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExactVanitySpell_CastsBoundDedicatedTransaction(bool includeAchievementEffect)
        {
            var fixture = new Fixture();
            if (includeAchievementEffect)
            {
                fixture.Effects.Insert(0, new Spell4EffectsEntry
                {
                    SpellId   = Fixture.SpellId,
                    EffectType = SpellEffectType.AchievementAdvance
                });
            }

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(Fixture.BaseId));

            fixture.Player.Verify(value => value.CastSpell(It.Is<ISpellParameters>(parameters =>
                ReferenceEquals(parameters.CharacterSpell, fixture.CharacterSpell.Object)
                && ReferenceEquals(parameters.SpellInfo, fixture.SpellInfo.Object)
                && !parameters.UserInitiatedSpellCast)), Times.Once);
            fixture.CharacterSpell.Verify(value => value.Cast(), Times.Never);
            fixture.SpellManager.Verify(value => value.GetSpellTier(It.IsAny<uint>()), Times.Never);
        }

        private static ClientSummonVanityPet CreateMessage(uint spell4BaseId)
        {
            var message = new ClientSummonVanityPet();
            typeof(ClientSummonVanityPet)
                .GetProperty(
                    nameof(ClientSummonVanityPet.Spell4BaseId),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, spell4BaseId);
            return message;
        }

        private sealed class Fixture
        {
            public const uint BaseId = 123u;
            public const uint SpellId = 456u;

            public ClientSummonVanityPetHandler Handler { get; } = new();
            public Mock<IWorldSession> Session { get; } = new(MockBehavior.Strict);
            public Mock<IPlayer> Player { get; } = new(MockBehavior.Strict);
            public Mock<ISpellManager> SpellManager { get; } = new(MockBehavior.Strict);
            public Mock<ICharacterSpell> CharacterSpell { get; } = new(MockBehavior.Strict);
            public Mock<ISpellBaseInfo> BaseInfo { get; } = new(MockBehavior.Strict);
            public Mock<ISpellInfo> SpellInfo { get; } = new(MockBehavior.Strict);
            public Spell4BaseEntry BaseEntry { get; } = new()
            {
                Id = BaseId
            };
            public Spell4Entry SpellEntry { get; } = new()
            {
                Id                        = SpellId,
                Spell4BaseIdBaseSpell     = BaseId
            };
            public Spell4EffectsEntry VanityEffect { get; } = new()
            {
                SpellId    = SpellId,
                EffectType = SpellEffectType.SummonVanityPet,
                DataBits00 = 789u
            };
            public List<Spell4EffectsEntry> Effects { get; }

            public Fixture()
            {
                Effects = [VanityEffect];
                BaseInfo.SetupGet(value => value.Entry).Returns(BaseEntry);
                SpellInfo.SetupGet(value => value.Entry).Returns(SpellEntry);
                SpellInfo.SetupGet(value => value.BaseInfo).Returns(BaseInfo.Object);
                SpellInfo.SetupGet(value => value.Effects).Returns(Effects);
                CharacterSpell.SetupGet(value => value.Owner).Returns(Player.Object);
                CharacterSpell.SetupGet(value => value.BaseInfo).Returns(BaseInfo.Object);
                CharacterSpell.SetupGet(value => value.SpellInfo).Returns(SpellInfo.Object);
                SpellManager.Setup(value => value.GetSpell(BaseId)).Returns(CharacterSpell.Object);
                Player.SetupGet(value => value.SpellManager).Returns(SpellManager.Object);
                Player.Setup(value => value.CastSpell(It.IsAny<ISpellParameters>()));
                Session.SetupGet(value => value.Player).Returns(Player.Object);
            }

            public void AssertRejected()
            {
                Assert.Throws<InvalidPacketValueException>(() =>
                    Handler.HandleMessage(Session.Object, CreateMessage(BaseId)));
            }
        }
    }
}
