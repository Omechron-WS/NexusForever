using System.Numerics;
using System.Reflection;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Map.Lock;
using NexusForever.Game.Map.Lock;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Housing;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model.Housing;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Housing;
using Moq;
using GameIdentity = NexusForever.Game.Abstract.Identity;

namespace NexusForever.WorldServer.Tests.Housing
{
    public sealed class ResidenceVisitHandlerTests
    {
        private const string PlayerName = "Target Player";
        private const string CommunityName = "Target Community";
        private const PropertyInfoId Property = (PropertyInfoId)17u;

        [Theory]
        [InlineData(ResidencePrivacyLevel.Public)]
        [InlineData(ResidencePrivacyLevel.NeighboursOnly)]
        [InlineData(ResidencePrivacyLevel.RoommatesOnly)]
        [InlineData(ResidencePrivacyLevel.Private)]
        public void PersonalOwner_CanVisitEveryPrivacyMode(ResidencePrivacyLevel privacyLevel)
        {
            VisitFixture fixture = CreateFixture(
                privacyLevel,
                owner: true,
                residenceType: ResidenceType.Residence);
            ResolveByPlayerName(fixture);

            Handle(fixture, CreateVisit(playerName: PlayerName));

            AssertTeleported(fixture);
        }

        [Fact]
        public void PersonalNonOwner_CanVisitPublicResidence()
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Public,
                owner: false,
                residenceType: ResidenceType.Residence);
            ResolveByPlayerName(fixture);

            Handle(fixture, CreateVisit(playerName: PlayerName));

            AssertTeleported(fixture);
        }

        [Theory]
        [InlineData(ResidencePrivacyLevel.NeighboursOnly)]
        [InlineData(ResidencePrivacyLevel.RoommatesOnly)]
        [InlineData(ResidencePrivacyLevel.Private)]
        public void PersonalNonOwner_RestrictedResidenceIsDeniedBeforeMapLock(
            ResidencePrivacyLevel privacyLevel)
        {
            VisitFixture fixture = CreateFixture(
                privacyLevel,
                owner: false,
                residenceType: ResidenceType.Residence);
            ResolveByPlayerName(fixture);

            Handle(fixture, CreateVisit(playerName: PlayerName));

            AssertDeniedBeforeMapLock(fixture);
        }

        [Theory]
        [InlineData(ResidencePrivacyLevel.NeighboursOnly)]
        [InlineData(ResidencePrivacyLevel.RoommatesOnly)]
        [InlineData(ResidencePrivacyLevel.Private)]
        public void RestrictedCommunity_IsDeniedBeforeMapLock(
            ResidencePrivacyLevel privacyLevel)
        {
            VisitFixture fixture = CreateFixture(
                privacyLevel,
                owner: false,
                residenceType: ResidenceType.Community);
            ResolveByCommunityName(fixture);

            Handle(fixture, CreateVisit(communityName: CommunityName));

            fixture.GlobalResidenceManager.Verify(
                manager => manager.GetCommunityByOwner(CommunityName),
                Times.Once);
            AssertDeniedBeforeMapLock(fixture);
        }

        [Fact]
        public void PublicCommunityName_UsesCommunityResolverAndTeleports()
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Public,
                owner: false,
                residenceType: ResidenceType.Community);
            ResolveByCommunityName(fixture);

            Handle(fixture, CreateVisit(communityName: CommunityName));

            fixture.GlobalResidenceManager.Verify(
                manager => manager.GetCommunityByOwner(CommunityName),
                Times.Once);
            fixture.GlobalResidenceManager.Verify(
                manager => manager.GetResidenceByOwner(It.IsAny<string>()),
                Times.Never);
            AssertTeleported(fixture);
        }

        [Fact]
        public void PersonalIdentity_UsesIdentityResolverAndTeleports()
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Public,
                owner: false,
                residenceType: ResidenceType.Residence);
            var targetIdentity = new GameIdentity
            {
                RealmId = 9,
                Id      = 900ul
            };
            fixture.GlobalResidenceManager
                .Setup(manager => manager.GetResidenceByOwner(
                    It.Is<GameIdentity>(identity => identity == targetIdentity)))
                .Returns(fixture.Residence.Object);
            ClientHousingVisit message = CreateVisit();
            message.IdentityToVisit.RealmId = targetIdentity.RealmId;
            message.IdentityToVisit.Id = targetIdentity.Id;

            Handle(fixture, message);

            fixture.GlobalResidenceManager.Verify(
                manager => manager.GetResidenceByOwner(
                    It.Is<GameIdentity>(identity => identity == targetIdentity)),
                Times.Once);
            AssertTeleported(fixture);
        }

        private static VisitFixture CreateFixture(
            ResidencePrivacyLevel privacyLevel,
            bool owner,
            ResidenceType residenceType)
        {
            var playerIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = 100ul
            };
            var ownerIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = owner ? 100ul : 101ul
            };
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Type).Returns(residenceType);
            residence.SetupGet(value => value.OwnerIdentity).Returns(ownerIdentity);
            residence.SetupGet(value => value.PrivacyLevel).Returns(privacyLevel);
            residence.SetupGet(value => value.PropertyInfoId).Returns(Property);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Identity).Returns(playerIdentity);
            player.SetupGet(value => value.Map).Returns(Mock.Of<IResidenceMapInstance>());
            player.Setup(value => value.CanTeleport()).Returns(true);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var globalResidenceManager = new Mock<IGlobalResidenceManager>();
            var globalGuildManager = new Mock<IGlobalGuildManager>();
            var mapLockManager = new Mock<IMapLockManager>();
            var mapLock = new Mock<IResidenceMapLock>();
            mapLockManager
                .Setup(manager => manager.GetResidenceLock(residence.Object))
                .Returns(mapLock.Object);

            var entry = new WorldEntry { Id = 88u };
            var destination = new Vector3(1.25f, 2.5f, 3.75f);
            var entrance = new Mock<IResidenceEntrance>();
            entrance.SetupGet(value => value.Entry).Returns(entry);
            entrance.SetupGet(value => value.Position).Returns(destination);
            entrance.SetupGet(value => value.Rotation).Returns(Quaternion.Identity);
            globalResidenceManager
                .Setup(manager => manager.GetResidenceEntrance(Property))
                .Returns(entrance.Object);

            var handler = new ClientHousingVisitHandler(
                globalResidenceManager.Object,
                globalGuildManager.Object,
                mapLockManager.Object);
            return new VisitFixture(
                handler,
                globalResidenceManager,
                mapLockManager,
                residence,
                mapLock,
                player,
                session,
                entry,
                destination);
        }

        private static void ResolveByPlayerName(VisitFixture fixture)
        {
            fixture.GlobalResidenceManager
                .Setup(manager => manager.GetResidenceByOwner(PlayerName))
                .Returns(fixture.Residence.Object);
        }

        private static void ResolveByCommunityName(VisitFixture fixture)
        {
            fixture.GlobalResidenceManager
                .Setup(manager => manager.GetCommunityByOwner(CommunityName))
                .Returns(fixture.Residence.Object);
        }

        private static void Handle(VisitFixture fixture, ClientHousingVisit message)
        {
            fixture.Handler.HandleMessage(fixture.Session.Object, message);
        }

        private static void AssertTeleported(VisitFixture fixture)
        {
            fixture.MapLockManager.Verify(
                manager => manager.GetResidenceLock(fixture.Residence.Object),
                Times.Once);
            fixture.Player.Verify(
                player => player.TeleportTo(
                    It.Is<IMapPosition>(position =>
                        ReferenceEquals(position.Info.Entry, fixture.Entry)
                        && ReferenceEquals(position.Info.MapLock, fixture.MapLock.Object)
                        && position.Position == fixture.Destination),
                    TeleportReason.Relocate),
                Times.Once);
            fixture.Player.Verify(
                player => player.TeleportTo(
                    It.IsAny<IMapPosition>(),
                    It.IsAny<TeleportReason>()),
                Times.Once);
        }

        private static void AssertDeniedBeforeMapLock(VisitFixture fixture)
        {
            fixture.MapLockManager.Verify(
                manager => manager.GetResidenceLock(It.IsAny<IResidence>()),
                Times.Never);
            fixture.GlobalResidenceManager.Verify(
                manager => manager.GetResidenceEntrance(It.IsAny<PropertyInfoId>()),
                Times.Never);
            fixture.Player.Verify(
                player => player.TeleportTo(
                    It.IsAny<IMapPosition>(),
                    It.IsAny<TeleportReason>()),
                Times.Never);
        }

        private static ClientHousingVisit CreateVisit(
            string playerName = null,
            string communityName = null)
        {
            var message = new ClientHousingVisit();
            SetProperty(message, nameof(ClientHousingVisit.PlayerToVisitName), playerName);
            SetProperty(message, nameof(ClientHousingVisit.CommunityToVisitName), communityName);
            return message;
        }

        private static void SetProperty(object instance, string propertyName, object value)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(instance, value);
        }

        private sealed record VisitFixture(
            ClientHousingVisitHandler Handler,
            Mock<IGlobalResidenceManager> GlobalResidenceManager,
            Mock<IMapLockManager> MapLockManager,
            Mock<IResidence> Residence,
            Mock<IResidenceMapLock> MapLock,
            Mock<IPlayer> Player,
            Mock<IWorldSession> Session,
            WorldEntry Entry,
            Vector3 Destination);
    }
}
