using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Character;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Static.Guild;
using NexusForever.Game.Static.Housing;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model.Housing;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Housing;
using GameIdentity = NexusForever.Game.Abstract.Identity;

namespace NexusForever.WorldServer.Tests.Housing
{
    public sealed class CommunityPrivacyHandlerTests
    {
        private const ulong PlayerId = 100ul;
        private const ulong LeaderId = 101ul;

        [Theory]
        [InlineData(CommunityPrivacyLevel.Public, false)]
        [InlineData(CommunityPrivacyLevel.Private, true)]
        public void SupportedTransition_UpdatesIndexBeforePersistedFlag(
            CommunityPrivacyLevel privacyLevel,
            bool expectedPrivate)
        {
            PrivacyFixture fixture = CreateFixture();
            List<string> events = [];
            ConfigureIndexCallback(fixture, privacyLevel, () => events.Add("index"));
            fixture.Community
                .Setup(community => community.SetCommunityPrivate(expectedPrivate))
                .Callback(() => events.Add("flag"));

            Handle(fixture, CreateMessage(fixture.CommunityIdentity, privacyLevel));

            Assert.Equal(["index", "flag"], events);
            AssertIndexTransition(fixture, privacyLevel, Times.Once());
            fixture.Community.Verify(
                community => community.SetCommunityPrivate(expectedPrivate),
                Times.Once);
            fixture.CharacterManager.Verify(
                manager => manager.GetCharacter(LeaderId),
                Times.Once);
        }

        [Theory]
        [InlineData(CommunityPrivacyLevel.Public, false)]
        [InlineData(CommunityPrivacyLevel.Private, true)]
        public void SupportedReplay_ReappliesIndexThenFlag(
            CommunityPrivacyLevel privacyLevel,
            bool expectedPrivate)
        {
            PrivacyFixture fixture = CreateFixture();

            Handle(fixture, CreateMessage(fixture.CommunityIdentity, privacyLevel));
            Handle(fixture, CreateMessage(fixture.CommunityIdentity, privacyLevel));

            AssertIndexTransition(fixture, privacyLevel, Times.Exactly(2));
            fixture.Community.Verify(
                community => community.SetCommunityPrivate(expectedPrivate),
                Times.Exactly(2));
        }

        [Theory]
        [InlineData(CommunityPrivacyLevel.Public, false)]
        [InlineData(CommunityPrivacyLevel.Private, true)]
        public void IndexFailure_PreventsPersistedFlagMutation(
            CommunityPrivacyLevel privacyLevel,
            bool expectedPrivate)
        {
            PrivacyFixture fixture = CreateFixture();
            var expected = new InvalidOperationException("index failed");
            ConfigureIndexCallback(fixture, privacyLevel, () => throw expected);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Handle(fixture, CreateMessage(fixture.CommunityIdentity, privacyLevel)));

            Assert.Same(expected, exception);
            fixture.Community.Verify(
                community => community.SetCommunityPrivate(expectedPrivate),
                Times.Never);
        }

        [Theory]
        [InlineData(CommunityPrivacyLevel.Public, false)]
        [InlineData(CommunityPrivacyLevel.Private, true)]
        public void FlagBroadcastFailure_HappensAfterIndexTransition(
            CommunityPrivacyLevel privacyLevel,
            bool expectedPrivate)
        {
            PrivacyFixture fixture = CreateFixture();
            List<string> events = [];
            var expected = new InvalidOperationException("broadcast failed");
            ConfigureIndexCallback(fixture, privacyLevel, () => events.Add("index"));
            fixture.Community
                .Setup(community => community.SetCommunityPrivate(expectedPrivate))
                .Callback(() =>
                {
                    events.Add("flag");
                    throw expected;
                });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Handle(fixture, CreateMessage(fixture.CommunityIdentity, privacyLevel)));

            Assert.Same(expected, exception);
            Assert.Equal(["index", "flag"], events);
            AssertIndexTransition(fixture, privacyLevel, Times.Once());
        }

        [Theory]
        [InlineData(AuthorityFailure.NonResidenceMap)]
        [InlineData(AuthorityFailure.MissingCommunity)]
        [InlineData(AuthorityFailure.PacketRealmMismatch)]
        [InlineData(AuthorityFailure.PacketIdMismatch)]
        [InlineData(AuthorityFailure.MissingResidence)]
        [InlineData(AuthorityFailure.WrongResidenceType)]
        [InlineData(AuthorityFailure.MissingGuildOwner)]
        [InlineData(AuthorityFailure.WrongGuildOwner)]
        [InlineData(AuthorityFailure.MissingResidenceMap)]
        [InlineData(AuthorityFailure.DifferentResidenceMap)]
        [InlineData(AuthorityFailure.MissingMember)]
        [InlineData(AuthorityFailure.MissingRank)]
        [InlineData(AuthorityFailure.MissingPermission)]
        [InlineData(AuthorityFailure.MissingLeaderId)]
        [InlineData(AuthorityFailure.MissingLeaderCharacter)]
        public void InvalidAuthority_RejectsBeforeIndexOrFlagMutation(AuthorityFailure failure)
        {
            PrivacyFixture fixture = CreateFixture();
            ClientHousingCommunityPrivacyLevel message = CreateMessage(
                fixture.CommunityIdentity,
                CommunityPrivacyLevel.Private);
            ApplyFailure(fixture, message, failure);

            Assert.Throws<InvalidPacketValueException>(() => Handle(fixture, message));

            AssertNoEffects(fixture);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        public void UnsupportedPrivacy_RejectsBeforeLeaderResolutionOrEffects(int rawPrivacy)
        {
            PrivacyFixture fixture = CreateFixture();

            Assert.Throws<InvalidPacketValueException>(() => Handle(
                fixture,
                CreateMessage(fixture.CommunityIdentity, (CommunityPrivacyLevel)rawPrivacy)));

            fixture.CharacterManager.Verify(
                manager => manager.GetCharacter(It.IsAny<ulong>()),
                Times.Never);
            AssertNoEffects(fixture);
        }

        private static PrivacyFixture CreateFixture()
        {
            var communityIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = 500ul
            };
            var residenceIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = 600ul
            };
            var residenceMap = new Mock<IResidenceMapInstance>();
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(residenceIdentity);
            residence.SetupGet(value => value.Type).Returns(ResidenceType.Community);
            residence.SetupGet(value => value.GuildOwnerIdentity).Returns(communityIdentity);
            residence.SetupGet(value => value.Map).Returns(residenceMap.Object);

            var rank = new Mock<IGuildRank>();
            rank.Setup(value => value.HasPermission(
                    GuildRankPermission.ChangeCommunityRemodelOptions))
                .Returns(true);
            var member = new Mock<IGuildMember>();
            member.SetupGet(value => value.Rank).Returns(rank.Object);
            var community = new Mock<ICommunity>();
            community.SetupGet(value => value.Identity).Returns(communityIdentity);
            community.SetupGet(value => value.Residence).Returns(residence.Object);
            community.SetupGet(value => value.LeaderId).Returns(LeaderId);
            community.Setup(value => value.GetMember(PlayerId)).Returns(member.Object);

            var guildManager = new Mock<IGuildManager>();
            guildManager
                .Setup(manager => manager.GetGuild<ICommunity>(GuildType.Community))
                .Returns(community.Object);
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(PlayerId);
            player.SetupGet(value => value.Map).Returns(residenceMap.Object);
            player.SetupGet(value => value.GuildManager).Returns(guildManager.Object);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            var leader = new Mock<ICharacter>();
            leader.SetupGet(value => value.Name).Returns("Persisted Leader");
            var characterManager = new Mock<ICharacterManager>();
            characterManager.Setup(manager => manager.GetCharacter(LeaderId)).Returns(leader.Object);

            var globalResidenceManager = new Mock<IGlobalResidenceManager>();
            var handler = new ClientHousingCommunityPrivacyLevelHandler(
                globalResidenceManager.Object,
                characterManager.Object);
            return new PrivacyFixture(
                handler,
                globalResidenceManager,
                characterManager,
                community,
                residence,
                rank,
                member,
                guildManager,
                player,
                session,
                residenceMap,
                communityIdentity);
        }

        private static void ApplyFailure(
            PrivacyFixture fixture,
            ClientHousingCommunityPrivacyLevel message,
            AuthorityFailure failure)
        {
            switch (failure)
            {
                case AuthorityFailure.NonResidenceMap:
                    fixture.Player.SetupGet(value => value.Map).Returns(Mock.Of<IBaseMap>());
                    break;
                case AuthorityFailure.MissingCommunity:
                    fixture.GuildManager
                        .Setup(manager => manager.GetGuild<ICommunity>(GuildType.Community))
                        .Returns((ICommunity)null);
                    break;
                case AuthorityFailure.PacketRealmMismatch:
                    message.GuildIdentity.RealmId++;
                    break;
                case AuthorityFailure.PacketIdMismatch:
                    message.GuildIdentity.Id++;
                    break;
                case AuthorityFailure.MissingResidence:
                    fixture.Community.SetupGet(value => value.Residence).Returns((IResidence)null);
                    break;
                case AuthorityFailure.WrongResidenceType:
                    fixture.Residence.SetupGet(value => value.Type).Returns(ResidenceType.Residence);
                    break;
                case AuthorityFailure.MissingGuildOwner:
                    fixture.Residence.SetupGet(value => value.GuildOwnerIdentity).Returns((GameIdentity)null);
                    break;
                case AuthorityFailure.WrongGuildOwner:
                    fixture.Residence.SetupGet(value => value.GuildOwnerIdentity).Returns(
                        new GameIdentity { RealmId = 1, Id = 501ul });
                    break;
                case AuthorityFailure.MissingResidenceMap:
                    fixture.Residence.SetupGet(value => value.Map).Returns((IResidenceMapInstance)null);
                    break;
                case AuthorityFailure.DifferentResidenceMap:
                    fixture.Residence.SetupGet(value => value.Map).Returns(Mock.Of<IResidenceMapInstance>());
                    break;
                case AuthorityFailure.MissingMember:
                    fixture.Community.Setup(value => value.GetMember(PlayerId)).Returns((IGuildMember)null);
                    break;
                case AuthorityFailure.MissingRank:
                    fixture.Member.SetupGet(value => value.Rank).Returns((IGuildRank)null);
                    break;
                case AuthorityFailure.MissingPermission:
                    fixture.Rank.Setup(value => value.HasPermission(
                            GuildRankPermission.ChangeCommunityRemodelOptions))
                        .Returns(false);
                    break;
                case AuthorityFailure.MissingLeaderId:
                    fixture.Community.SetupGet(value => value.LeaderId).Returns((ulong?)null);
                    break;
                case AuthorityFailure.MissingLeaderCharacter:
                    fixture.CharacterManager.Setup(manager => manager.GetCharacter(LeaderId)).Returns((ICharacter)null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }

        private static void ConfigureIndexCallback(
            PrivacyFixture fixture,
            CommunityPrivacyLevel privacyLevel,
            Action callback)
        {
            if (privacyLevel == CommunityPrivacyLevel.Public)
            {
                fixture.GlobalResidenceManager
                    .Setup(manager => manager.RegisterCommunityVisits(
                        fixture.Residence.Object,
                        fixture.Community.Object,
                        "Persisted Leader"))
                    .Callback(callback);
            }
            else
            {
                fixture.GlobalResidenceManager
                    .Setup(manager => manager.DeregisterCommunityVists(
                        fixture.Residence.Object.Identity))
                    .Callback(callback);
            }
        }

        private static void AssertIndexTransition(
            PrivacyFixture fixture,
            CommunityPrivacyLevel privacyLevel,
            Times times)
        {
            if (privacyLevel == CommunityPrivacyLevel.Public)
            {
                fixture.GlobalResidenceManager.Verify(
                    manager => manager.RegisterCommunityVisits(
                        fixture.Residence.Object,
                        fixture.Community.Object,
                        "Persisted Leader"),
                    times);
                fixture.GlobalResidenceManager.Verify(
                    manager => manager.DeregisterCommunityVists(It.IsAny<GameIdentity>()),
                    Times.Never);
            }
            else
            {
                fixture.GlobalResidenceManager.Verify(
                    manager => manager.RegisterCommunityVisits(
                        It.IsAny<IResidence>(),
                        It.IsAny<ICommunity>(),
                        It.IsAny<string>()),
                    Times.Never);
                fixture.GlobalResidenceManager.Verify(
                    manager => manager.DeregisterCommunityVists(fixture.Residence.Object.Identity),
                    times);
            }
        }

        private static void AssertNoEffects(PrivacyFixture fixture)
        {
            fixture.GlobalResidenceManager.Verify(
                manager => manager.RegisterCommunityVisits(
                    It.IsAny<IResidence>(),
                    It.IsAny<ICommunity>(),
                    It.IsAny<string>()),
                Times.Never);
            fixture.GlobalResidenceManager.Verify(
                manager => manager.DeregisterCommunityVists(It.IsAny<GameIdentity>()),
                Times.Never);
            fixture.Community.Verify(
                community => community.SetCommunityPrivate(It.IsAny<bool>()),
                Times.Never);
        }

        private static void Handle(
            PrivacyFixture fixture,
            ClientHousingCommunityPrivacyLevel message)
        {
            fixture.Handler.HandleMessage(fixture.Session.Object, message);
        }

        private static ClientHousingCommunityPrivacyLevel CreateMessage(
            GameIdentity communityIdentity,
            CommunityPrivacyLevel privacyLevel)
        {
            var message = new ClientHousingCommunityPrivacyLevel();
            message.GuildIdentity.RealmId = communityIdentity.RealmId;
            message.GuildIdentity.Id = communityIdentity.Id;
            PropertyInfo property = typeof(ClientHousingCommunityPrivacyLevel).GetProperty(
                nameof(ClientHousingCommunityPrivacyLevel.PrivacyLevel),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(message, privacyLevel);
            return message;
        }

        public enum AuthorityFailure
        {
            NonResidenceMap,
            MissingCommunity,
            PacketRealmMismatch,
            PacketIdMismatch,
            MissingResidence,
            WrongResidenceType,
            MissingGuildOwner,
            WrongGuildOwner,
            MissingResidenceMap,
            DifferentResidenceMap,
            MissingMember,
            MissingRank,
            MissingPermission,
            MissingLeaderId,
            MissingLeaderCharacter
        }

        private sealed record PrivacyFixture(
            ClientHousingCommunityPrivacyLevelHandler Handler,
            Mock<IGlobalResidenceManager> GlobalResidenceManager,
            Mock<ICharacterManager> CharacterManager,
            Mock<ICommunity> Community,
            Mock<IResidence> Residence,
            Mock<IGuildRank> Rank,
            Mock<IGuildMember> Member,
            Mock<IGuildManager> GuildManager,
            Mock<IPlayer> Player,
            Mock<IWorldSession> Session,
            Mock<IResidenceMapInstance> ResidenceMap,
            GameIdentity CommunityIdentity);
    }
}
