using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.Session;

namespace NexusForever.Game.Tests.Entity
{
    public sealed class ResurrectionAdmissionTests
    {
        private const uint MaximumHealth = 101u;

        [Fact]
        public void Resurrect_UnshownChoiceRejectsBeforeCostOrVitals()
        {
            ResurrectionFixture fixture = CreateFixture();

            fixture.Manager.Resurrect(ResurrectionType.WakeHere);

            AssertNoResurrectionMutation(fixture);
            fixture.Session.Verify(value => value.EnqueueMessageEncrypted(It.IsAny<NexusForever.Network.Message.IWritable>()), Times.Never);
        }

        public static IEnumerable<object[]> InvalidOfferedChoices()
        {
            yield return new object[] { ResurrectionType.None, ResurrectionType.None };
            yield return new object[] { (ResurrectionType)8, (ResurrectionType)8 };
            yield return new object[]
            {
                ResurrectionType.WakeHere | ResurrectionType.WakeHereServiceToken,
                ResurrectionType.None
            };
            yield return new object[] { ResurrectionType.Holocrypt, ResurrectionType.Holocrypt };
            yield return new object[] { ResurrectionType.ExitInstance, ResurrectionType.ExitInstance };
        }

        [Theory]
        [MemberData(nameof(InvalidOfferedChoices))]
        public void Resurrect_InvalidOrUnimplementedOfferedChoiceRejectsBeforeCostOrVitals(
            ResurrectionType requestedType,
            ResurrectionType mapType)
        {
            ResurrectionFixture fixture = CreateFixture(mapType);
            fixture.Manager.ShowResurrection();
            ClearInvocations(fixture);

            fixture.Manager.Resurrect(requestedType);

            AssertNoResurrectionMutation(fixture);
        }

        [Fact]
        public void Resurrect_WakeHerePreservesCostThenHalfHealthAndShieldOrderAndConsumesOffer()
        {
            ResurrectionFixture fixture = CreateFixture();
            var calls = new List<string>();
            fixture.CurrencyManager
                .Setup(value => value.CanAfford(CurrencyType.Credits, 0ul))
                .Callback(() => calls.Add("afford"))
                .Returns(true);
            fixture.CurrencyManager
                .Setup(value => value.CurrencySubtractAmount(CurrencyType.Credits, 0ul, false))
                .Callback(() => calls.Add("subtract"));
            fixture.Owner
                .Setup(value => value.ModifyHealth(MaximumHealth / 2u, DamageType.Heal, null))
                .Callback(() => calls.Add("health"));
            fixture.Owner
                .SetupSet(value => value.Shield = 0u)
                .Callback(() => calls.Add("shield"));

            fixture.Manager.ShowResurrection();
            fixture.Manager.Resurrect(ResurrectionType.WakeHere);

            Assert.Equal(new[] { "afford", "subtract", "health", "shield" }, calls);
            fixture.Owner.Verify(value => value.ModifyHealth(MaximumHealth / 2u, DamageType.Heal, null), Times.Once);
            fixture.Owner.VerifySet(value => value.Shield = 0u, Times.Once);

            calls.Clear();
            ClearInvocations(fixture);
            fixture.Manager.Resurrect(ResurrectionType.WakeHere);

            Assert.Empty(calls);
            AssertNoResurrectionMutation(fixture);
            fixture.Owner.VerifyGet(value => value.IsAlive, Times.Once);
        }

        [Fact]
        public void Resurrect_SpellCasterLocationWithoutCurrentRequestRejects()
        {
            ResurrectionFixture fixture = CreateFixture();
            fixture.Manager.ShowResurrection();
            ClearInvocations(fixture);

            fixture.Manager.Resurrect(ResurrectionType.SpellCasterLocation);

            AssertNoResurrectionMutation(fixture);
        }

        [Fact]
        public void Resurrect_SpellCasterLocationWithCurrentRequestSucceedsAndConsumesOffer()
        {
            ResurrectionFixture fixture = CreateFixture();
            fixture.Manager.ShowResurrection();
            fixture.Manager.ResurrectRequest(77u);
            ClearInvocations(fixture);

            fixture.Manager.Resurrect(ResurrectionType.SpellCasterLocation);

            fixture.Owner.Verify(value => value.ModifyHealth(MaximumHealth / 2u, DamageType.Heal, null), Times.Once);
            fixture.Owner.VerifySet(value => value.Shield = 0u, Times.Once);
            fixture.CurrencyManager.Verify(value => value.CanAfford(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);

            ClearInvocations(fixture);
            fixture.Manager.Resurrect(ResurrectionType.SpellCasterLocation);

            AssertNoResurrectionMutation(fixture);
            fixture.Owner.VerifyGet(value => value.IsAlive, Times.Once);
        }

        private static ResurrectionFixture CreateFixture(ResurrectionType mapType = ResurrectionType.None)
        {
            var session = new Mock<IGameSession>();
            var map = new Mock<IBaseMap>();
            map.Setup(value => value.GetResurrectionType()).Returns(mapType);

            var currencyManager = new Mock<ICurrencyManager>();
            var owner = new Mock<IPlayer>();
            owner.SetupGet(value => value.IsAlive).Returns(false);
            owner.SetupGet(value => value.MaxHealth).Returns(MaximumHealth);
            owner.SetupProperty(value => value.Shield, 37u);
            owner.SetupGet(value => value.Map).Returns(map.Object);
            owner.SetupGet(value => value.Session).Returns(session.Object);
            owner.SetupGet(value => value.CurrencyManager).Returns(currencyManager.Object);

            return new ResurrectionFixture(
                new ResurrectionManager(owner.Object),
                owner,
                session,
                currencyManager);
        }

        private static void AssertNoResurrectionMutation(ResurrectionFixture fixture)
        {
            fixture.CurrencyManager.Verify(value => value.CanAfford(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>()), Times.Never);
            fixture.CurrencyManager.Verify(value => value.CurrencySubtractAmount(
                It.IsAny<CurrencyType>(), It.IsAny<ulong>(), It.IsAny<bool>()), Times.Never);
            fixture.Owner.Verify(value => value.ModifyHealth(
                It.IsAny<uint>(), It.IsAny<DamageType>(), It.IsAny<IUnitEntity>()), Times.Never);
            fixture.Owner.VerifySet(value => value.Shield = 0u, Times.Never);
        }

        private static void ClearInvocations(ResurrectionFixture fixture)
        {
            fixture.Owner.Invocations.Clear();
            fixture.Session.Invocations.Clear();
            fixture.CurrencyManager.Invocations.Clear();
        }

        private sealed record ResurrectionFixture(
            ResurrectionManager Manager,
            Mock<IPlayer> Owner,
            Mock<IGameSession> Session,
            Mock<ICurrencyManager> CurrencyManager);
    }
}
