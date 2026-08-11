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
using NexusForever.Game.Static.Guild;
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

        [Fact]
        public void PublicCommunityName_UsesLiveGuildFlagAndTeleports()
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Private,
                owner: false,
                residenceType: ResidenceType.Community,
                communityPrivate: false);
            ResolveByCommunityName(fixture);

            Handle(fixture, CreateVisit(communityName: CommunityName));

            fixture.GlobalResidenceManager.Verify(
                manager => manager.GetCommunityByOwner(CommunityName),
                Times.Once);
            fixture.GlobalResidenceManager.Verify(
                manager => manager.GetResidenceByOwner(It.IsAny<string>()),
                Times.Never);
            fixture.GlobalGuildManager.Verify(
                manager => manager.GetGuild(
                    It.Is<GameIdentity>(identity => identity == fixture.CommunityIdentity)),
                Times.Once);
            fixture.Community.Verify(
                community => community.GetMember(It.IsAny<ulong>()),
                Times.Never);
            AssertTeleported(fixture);
        }

        [Fact]
        public void PrivateCommunity_CurrentMemberCanVisit()
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Public,
                owner: false,
                residenceType: ResidenceType.Community,
                communityPrivate: true,
                communityMember: true);
            ResolveByCommunityName(fixture);

            Handle(fixture, CreateVisit(communityName: CommunityName));

            fixture.Community.Verify(
                community => community.GetMember(fixture.Player.Object.CharacterId),
                Times.Once);
            AssertTeleported(fixture);
        }

        [Fact]
        public void PrivateCommunity_NonMemberIsDeniedBeforeMapLock()
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Public,
                owner: false,
                residenceType: ResidenceType.Community,
                communityPrivate: true,
                communityMember: false);
            ResolveByCommunityName(fixture);

            Handle(fixture, CreateVisit(communityName: CommunityName));

            fixture.Community.Verify(
                community => community.GetMember(fixture.Player.Object.CharacterId),
                Times.Once);
            AssertDeniedBeforeMapLock(fixture);
        }

        [Theory]
        [InlineData(CommunityLinkFailure.MissingGuildOwner)]
        [InlineData(CommunityLinkFailure.WrongGuildIdentity)]
        [InlineData(CommunityLinkFailure.WrongResidence)]
        public void MalformedCommunityLink_IsDeniedBeforeMapLock(CommunityLinkFailure failure)
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Public,
                owner: false,
                residenceType: ResidenceType.Community);
            ResolveByCommunityName(fixture);
            switch (failure)
            {
                case CommunityLinkFailure.MissingGuildOwner:
                    fixture.Residence.SetupGet(value => value.GuildOwnerIdentity).Returns((GameIdentity)null);
                    break;
                case CommunityLinkFailure.WrongGuildIdentity:
                    fixture.Community.SetupGet(value => value.Identity).Returns(
                        new GameIdentity { RealmId = 1, Id = 501ul });
                    break;
                case CommunityLinkFailure.WrongResidence:
                    fixture.Community.SetupGet(value => value.Residence).Returns(Mock.Of<IResidence>());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }

            Handle(fixture, CreateVisit(communityName: CommunityName));

            AssertDeniedBeforeMapLock(fixture);
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

        [Fact]
        public void CommunityIdentity_WithDefaultPersonalIdentity_UsesCommunityResolverAndTeleports()
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Private,
                owner: false,
                residenceType: ResidenceType.Community,
                communityPrivate: false);
            ClientHousingVisit message = CreateVisit();
            message.CommunityToVisitIdentity.RealmId = fixture.CommunityIdentity.RealmId;
            message.CommunityToVisitIdentity.Id = fixture.CommunityIdentity.Id;

            Handle(fixture, message);

            fixture.GlobalResidenceManager.Verify(
                manager => manager.GetResidenceByOwner(It.IsAny<GameIdentity>()),
                Times.Never);
            fixture.GlobalGuildManager.Verify(
                manager => manager.GetGuild(
                    It.Is<GameIdentity>(identity => identity == fixture.CommunityIdentity)),
                Times.Exactly(2));
            AssertTeleported(fixture);
        }

        [Fact]
        public void CommunityIdentity_WrongRealmIsDeniedBeforeMapLock()
        {
            VisitFixture fixture = CreateFixture(
                ResidencePrivacyLevel.Public,
                owner: false,
                residenceType: ResidenceType.Community);
            ClientHousingVisit message = CreateVisit();
            message.CommunityToVisitIdentity.RealmId = (ushort)(fixture.CommunityIdentity.RealmId + 1);
            message.CommunityToVisitIdentity.Id = fixture.CommunityIdentity.Id;

            Handle(fixture, message);

            AssertDeniedBeforeMapLock(fixture);
        }

        private static VisitFixture CreateFixture(
            ResidencePrivacyLevel privacyLevel,
            bool owner,
            ResidenceType residenceType,
            bool communityPrivate = false,
            bool communityMember = false)
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
            var residenceIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = 700ul
            };
            var communityIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = 500ul
            };
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            residence.SetupGet(value => value.Type).Returns(residenceType);
            residence.SetupGet(value => value.OwnerIdentity).Returns(ownerIdentity);
            residence.SetupGet(value => value.GuildOwnerIdentity).Returns(
                residenceType == ResidenceType.Community ? communityIdentity : null);
            residence.SetupGet(value => value.PrivacyLevel).Returns(privacyLevel);
            residence.SetupGet(value => value.PropertyInfoId).Returns(Property);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Identity).Returns(playerIdentity);
            player.SetupGet(value => value.CharacterId).Returns(playerIdentity.Id);
            player.SetupGet(value => value.Map).Returns(Mock.Of<IResidenceMapInstance>());
            player.Setup(value => value.CanTeleport()).Returns(true);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var globalResidenceManager = new Mock<IGlobalResidenceManager>();
            var globalGuildManager = new Mock<IGlobalGuildManager>();
            var community = new Mock<ICommunity>();
            community.SetupGet(value => value.Identity).Returns(communityIdentity);
            community.SetupGet(value => value.Residence).Returns(residence.Object);
            community.SetupGet(value => value.Flags).Returns(
                communityPrivate ? GuildFlag.CommunityPrivate : GuildFlag.None);
            community.Setup(value => value.GetMember(playerIdentity.Id)).Returns(
                communityMember ? Mock.Of<IGuildMember>() : null);
            globalGuildManager
                .Setup(manager => manager.GetGuild(
                    It.Is<GameIdentity>(identity => identity == communityIdentity)))
                .Returns(community.Object);
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
                globalGuildManager,
                mapLockManager,
                residence,
                community,
                mapLock,
                player,
                session,
                entry,
                destination,
                communityIdentity);
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

        public enum CommunityLinkFailure
        {
            MissingGuildOwner,
            WrongGuildIdentity,
            WrongResidence
        }

        private sealed record VisitFixture(
            ClientHousingVisitHandler Handler,
            Mock<IGlobalResidenceManager> GlobalResidenceManager,
            Mock<IGlobalGuildManager> GlobalGuildManager,
            Mock<IMapLockManager> MapLockManager,
            Mock<IResidence> Residence,
            Mock<ICommunity> Community,
            Mock<IResidenceMapLock> MapLock,
            Mock<IPlayer> Player,
            Mock<IWorldSession> Session,
            WorldEntry Entry,
            Vector3 Destination,
            GameIdentity CommunityIdentity);
    }
}
