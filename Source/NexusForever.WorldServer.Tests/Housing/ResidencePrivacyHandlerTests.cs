using System.Reflection;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Housing;
using NexusForever.Game.Static.Housing;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Housing;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Housing;
using Moq;
using GameIdentity = NexusForever.Game.Abstract.Identity;

namespace NexusForever.WorldServer.Tests.Housing
{
    public sealed class ResidencePrivacyHandlerTests
    {
        private const ulong ResidenceId = 700ul;

        [Fact]
        public void Handler_ValidResidenceMap_DelegatesValueExactlyOnce()
        {
            var residence = new Mock<IResidence>();
            var residenceManager = new Mock<IResidenceManager>();
            residenceManager.SetupGet(value => value.Residence).Returns(residence.Object);
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Map).Returns(Mock.Of<IResidenceMapInstance>());
            player.SetupGet(value => value.ResidenceManager).Returns(residenceManager.Object);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);
            ResidencePrivacyLevel value = (ResidencePrivacyLevel)(-1);

            new ClientHousingResidencePrivacyLevelHandler().HandleMessage(
                session.Object,
                CreateMessage(value));

            residenceManager.Verify(
                manager => manager.SetResidencePrivacy(value),
                Times.Once);
            residenceManager.Verify(
                manager => manager.SetResidencePrivacy(It.IsAny<ResidencePrivacyLevel>()),
                Times.Once);
        }

        [Fact]
        public void Handler_NonResidenceMap_RejectsBeforeDelegation()
        {
            var residenceManager = new Mock<IResidenceManager>();
            residenceManager.SetupGet(value => value.Residence).Returns(Mock.Of<IResidence>());
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Map).Returns(Mock.Of<IBaseMap>());
            player.SetupGet(value => value.ResidenceManager).Returns(residenceManager.Object);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientHousingResidencePrivacyLevelHandler().HandleMessage(
                    session.Object,
                    CreateMessage(ResidencePrivacyLevel.Public)));

            residenceManager.Verify(
                manager => manager.SetResidencePrivacy(It.IsAny<ResidencePrivacyLevel>()),
                Times.Never);
        }

        [Fact]
        public void Handler_MissingResidence_RejectsBeforeDelegation()
        {
            var residenceManager = new Mock<IResidenceManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Map).Returns(Mock.Of<IResidenceMapInstance>());
            player.SetupGet(value => value.ResidenceManager).Returns(residenceManager.Object);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            Assert.Throws<InvalidPacketValueException>(() =>
                new ClientHousingResidencePrivacyLevelHandler().HandleMessage(
                    session.Object,
                    CreateMessage(ResidencePrivacyLevel.Public)));

            residenceManager.Verify(
                manager => manager.SetResidencePrivacy(It.IsAny<ResidencePrivacyLevel>()),
                Times.Never);
        }

        [Theory]
        [InlineData(ResidencePrivacyLevel.Public, ResidencePrivacyLevel.Private)]
        [InlineData(ResidencePrivacyLevel.Private, ResidencePrivacyLevel.Public)]
        public void SupportedOwnerMutation_UpdatesStateIndexAndBasics(
            ResidencePrivacyLevel requested,
            ResidencePrivacyLevel initial)
        {
            PrivacyFixture fixture = CreateManagerFixture(initial);

            Handle(fixture, requested);

            Assert.Equal(requested, fixture.Residence.Object.PrivacyLevel);
            fixture.Residence.VerifySet(
                value => value.PrivacyLevel = requested,
                Times.Once);
            AssertIndexTransition(fixture, requested);
            AssertHousingBasics(fixture, requested);
        }

        [Theory]
        [InlineData(ResidencePrivacyLevel.Public)]
        [InlineData(ResidencePrivacyLevel.Private)]
        public void SupportedReplay_IsHandledByResidenceManagerAndResynchronised(
            ResidencePrivacyLevel requested)
        {
            PrivacyFixture fixture = CreateManagerFixture(requested);

            Handle(fixture, requested);

            Assert.Equal(requested, fixture.Residence.Object.PrivacyLevel);
            fixture.Residence.VerifySet(
                value => value.PrivacyLevel = requested,
                Times.Once);
            AssertIndexTransition(fixture, requested);
            AssertHousingBasics(fixture, requested);
        }

        [Theory]
        [InlineData((int)ResidencePrivacyLevel.NeighboursOnly)]
        [InlineData((int)ResidencePrivacyLevel.RoommatesOnly)]
        [InlineData(-1)]
        [InlineData(4)]
        public void UnsupportedValue_DoesNotMutateAndResynchronisesCurrentBasics(int rawValue)
        {
            PrivacyFixture fixture = CreateManagerFixture(ResidencePrivacyLevel.Private);

            Handle(fixture, (ResidencePrivacyLevel)rawValue);

            Assert.Equal(ResidencePrivacyLevel.Private, fixture.Residence.Object.PrivacyLevel);
            AssertNoResidenceMutation(fixture);
            AssertHousingBasics(fixture, ResidencePrivacyLevel.Private);
        }

        [Theory]
        [InlineData(false, true, ResidenceType.Residence)]
        [InlineData(true, false, ResidenceType.Residence)]
        [InlineData(true, true, ResidenceType.Community)]
        public void AuthorityDenial_DoesNotMutateAndResynchronisesCurrentBasics(
            bool matchingOwner,
            bool sameMap,
            ResidenceType residenceType)
        {
            PrivacyFixture fixture = CreateManagerFixture(
                ResidencePrivacyLevel.Private,
                matchingOwner,
                sameMap,
                residenceType);

            Handle(fixture, ResidencePrivacyLevel.Public);

            Assert.Equal(ResidencePrivacyLevel.Private, fixture.Residence.Object.PrivacyLevel);
            AssertNoResidenceMutation(fixture);
            AssertHousingBasics(fixture, ResidencePrivacyLevel.Private);
        }

        private static void Handle(PrivacyFixture fixture, ResidencePrivacyLevel privacyLevel)
        {
            new ClientHousingResidencePrivacyLevelHandler().HandleMessage(
                fixture.Session.Object,
                CreateMessage(privacyLevel));
        }

        private static PrivacyFixture CreateManagerFixture(
            ResidencePrivacyLevel initialPrivacy,
            bool matchingOwner = true,
            bool sameMap = true,
            ResidenceType residenceType = ResidenceType.Residence)
        {
            var playerIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = 42ul
            };
            var residenceOwnerIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = matchingOwner ? 42ul : 43ul
            };
            var residenceIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = ResidenceId
            };
            var playerMap = new Mock<IResidenceMapInstance>();
            var residenceMap = sameMap ? playerMap : new Mock<IResidenceMapInstance>();

            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            residence.SetupGet(value => value.OwnerIdentity).Returns(residenceOwnerIdentity);
            residence.SetupGet(value => value.Type).Returns(residenceType);
            residence.SetupGet(value => value.Map).Returns(residenceMap.Object);
            residence.SetupProperty(value => value.PrivacyLevel, initialPrivacy);

            var globalResidenceManager = new Mock<IGlobalResidenceManager>();
            globalResidenceManager
                .Setup(manager => manager.GetResidenceByOwner(
                    It.Is<GameIdentity>(identity => identity == playerIdentity)))
                .Returns(residence.Object);

            var session = new Mock<IWorldSession>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Identity).Returns(playerIdentity);
            player.SetupGet(value => value.Name).Returns("Owner");
            player.SetupGet(value => value.Map).Returns(playerMap.Object);
            player.SetupGet(value => value.Session).Returns(session.Object);

            var residenceManager = new ResidenceManager(globalResidenceManager.Object);
            residenceManager.Initialise(player.Object);
            player.SetupGet(value => value.ResidenceManager).Returns(residenceManager);
            session.SetupGet(value => value.Player).Returns(player.Object);

            return new PrivacyFixture(
                globalResidenceManager,
                residence,
                session);
        }

        private static void AssertIndexTransition(
            PrivacyFixture fixture,
            ResidencePrivacyLevel privacyLevel)
        {
            if (privacyLevel == ResidencePrivacyLevel.Public)
            {
                fixture.GlobalResidenceManager.Verify(
                    manager => manager.RegisterResidenceVists(
                        fixture.Residence.Object,
                        "Owner"),
                    Times.Once);
                fixture.GlobalResidenceManager.Verify(
                    manager => manager.DeregisterResidenceVists(It.IsAny<GameIdentity>()),
                    Times.Never);
            }
            else
            {
                fixture.GlobalResidenceManager.Verify(
                    manager => manager.RegisterResidenceVists(
                        It.IsAny<IResidence>(),
                        It.IsAny<string>()),
                    Times.Never);
                fixture.GlobalResidenceManager.Verify(
                    manager => manager.DeregisterResidenceVists(
                        It.Is<GameIdentity>(identity => identity == fixture.Residence.Object.Identity)),
                    Times.Once);
            }
        }

        private static void AssertNoResidenceMutation(PrivacyFixture fixture)
        {
            fixture.Residence.VerifySet(
                value => value.PrivacyLevel = It.IsAny<ResidencePrivacyLevel>(),
                Times.Never);
            fixture.GlobalResidenceManager.Verify(
                manager => manager.RegisterResidenceVists(
                    It.IsAny<IResidence>(),
                    It.IsAny<string>()),
                Times.Never);
            fixture.GlobalResidenceManager.Verify(
                manager => manager.DeregisterResidenceVists(It.IsAny<GameIdentity>()),
                Times.Never);
        }

        private static void AssertHousingBasics(
            PrivacyFixture fixture,
            ResidencePrivacyLevel privacyLevel)
        {
            ServerHousingBasics.ResidencePrivacyLevelFlags expectedFlags = privacyLevel switch
            {
                ResidencePrivacyLevel.Public  => ServerHousingBasics.ResidencePrivacyLevelFlags.Public,
                ResidencePrivacyLevel.Private => ServerHousingBasics.ResidencePrivacyLevelFlags.Private,
                _ => throw new ArgumentOutOfRangeException(nameof(privacyLevel))
            };

            fixture.Session.Verify(
                session => session.EnqueueMessageEncrypted(
                    It.Is<ServerHousingBasics>(basics =>
                        basics.ResidenceId == ResidenceId
                        && basics.NeighbourhoodId == 0ul
                        && basics.PrivacyLevel == expectedFlags)),
                Times.Once);
            fixture.Session.Verify(
                session => session.EnqueueMessageEncrypted(It.IsAny<IWritable>()),
                Times.Once);
        }

        private static ClientHousingSetPrivacyLevel CreateMessage(
            ResidencePrivacyLevel privacyLevel)
        {
            var message = new ClientHousingSetPrivacyLevel();
            PropertyInfo property = typeof(ClientHousingSetPrivacyLevel).GetProperty(
                nameof(ClientHousingSetPrivacyLevel.PrivacyLevel),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(message, privacyLevel);
            return message;
        }

        private sealed record PrivacyFixture(
            Mock<IGlobalResidenceManager> GlobalResidenceManager,
            Mock<IResidence> Residence,
            Mock<IWorldSession> Session);
    }
}
