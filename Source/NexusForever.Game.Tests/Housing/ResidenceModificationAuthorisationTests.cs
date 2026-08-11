using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Housing;
using NexusForever.Game.Static.Guild;
using NexusForever.Game.Static.Housing;
using NexusForever.GameTable;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Housing
{
    public sealed class ResidenceModificationAuthorisationTests
    {
        private const ushort RealmId = 1;
        private const ulong OwnerId = 10ul;
        private const ulong CommunityId = 20ul;
        private const ulong CharacterId = 30ul;

        [Fact]
        public void PersonalResidence_ExactOwnerOnExactMap_IsAllowed()
        {
            Residence residence = CreatePersonalResidence();
            var map = new Mock<IResidenceMapInstance>();
            residence.Map = map.Object;
            Mock<IPlayer> player = CreatePlayer(map.Object, OwnerId);

            Assert.True(residence.CanModifyResidence(player.Object));
        }

        [Fact]
        public void PersonalResidence_MissingLiveMap_IsDenied()
        {
            Residence residence = CreatePersonalResidence();
            Mock<IPlayer> player = CreatePlayer(Mock.Of<IResidenceMapInstance>(), OwnerId);

            Assert.False(residence.CanModifyResidence(player.Object));
        }

        [Fact]
        public void PersonalResidence_DifferentLiveMap_IsDenied()
        {
            Residence residence = CreatePersonalResidence();
            residence.Map = Mock.Of<IResidenceMapInstance>();
            Mock<IPlayer> player = CreatePlayer(Mock.Of<IResidenceMapInstance>(), OwnerId);

            Assert.False(residence.CanModifyResidence(player.Object));
        }

        [Fact]
        public void PersonalResidence_DifferentOwner_IsDenied()
        {
            Residence residence = CreatePersonalResidence();
            var map = new Mock<IResidenceMapInstance>();
            residence.Map = map.Object;
            Mock<IPlayer> player = CreatePlayer(map.Object, OwnerId + 1ul);

            Assert.False(residence.CanModifyResidence(player.Object));
        }

        [Fact]
        public void Residence_NullPlayer_IsDenied()
        {
            Residence residence = CreatePersonalResidence();
            residence.Map = Mock.Of<IResidenceMapInstance>();

            Assert.False(residence.CanModifyResidence(null));
        }

        [Fact]
        public void CommunityResidence_ExactLinkedMemberWithPermission_IsAllowed()
        {
            CommunityFixture fixture = CreateCommunityFixture();

            Assert.True(fixture.Residence.CanModifyResidence(fixture.Player.Object));
        }

        [Fact]
        public void CommunityResidence_DifferentLiveMap_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            fixture.Player
                .SetupGet(player => player.Map)
                .Returns(Mock.Of<IResidenceMapInstance>());

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
        }

        [Fact]
        public void CommunityResidence_MissingOwnerIdentity_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            fixture.Residence.GuildOwnerIdentity = null;

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
        }

        [Fact]
        public void CommunityResidence_MissingPlayerCommunity_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            fixture.GuildManager
                .Setup(manager => manager.GetGuild<ICommunity>(GuildType.Community))
                .Returns((ICommunity)null);

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
        }

        [Fact]
        public void CommunityResidence_UnrelatedPlayerCommunity_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            fixture.Community.SetupGet(community => community.Identity).Returns(new Identity
            {
                RealmId = RealmId,
                Id      = CommunityId + 1ul
            });

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
            fixture.Community.Verify(
                community => community.GetMember(It.IsAny<ulong>()),
                Times.Never);
        }

        [Fact]
        public void CommunityResidence_StaleResidenceReference_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            var staleResidence = new Mock<IResidence>();
            staleResidence.SetupGet(residence => residence.Identity).Returns(fixture.Residence.Identity);
            fixture.Community.SetupGet(community => community.Residence).Returns(staleResidence.Object);

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
            fixture.Community.Verify(
                community => community.GetMember(It.IsAny<ulong>()),
                Times.Never);
        }

        [Fact]
        public void CommunityResidence_MissingResidenceReference_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            fixture.Community.SetupGet(community => community.Residence).Returns((IResidence)null);

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
            fixture.Community.Verify(
                community => community.GetMember(It.IsAny<ulong>()),
                Times.Never);
        }

        [Fact]
        public void CommunityResidence_MissingMember_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            fixture.Community
                .Setup(community => community.GetMember(CharacterId))
                .Returns((IGuildMember)null);

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
        }

        [Fact]
        public void CommunityResidence_MissingRank_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            fixture.Member.SetupGet(member => member.Rank).Returns((IGuildRank)null);

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
        }

        [Fact]
        public void CommunityResidence_MissingDecoratePermission_IsDenied()
        {
            CommunityFixture fixture = CreateCommunityFixture();
            fixture.Rank
                .Setup(rank => rank.HasPermission(GuildRankPermission.DecorateCommunity))
                .Returns(false);

            Assert.False(fixture.Residence.CanModifyResidence(fixture.Player.Object));
        }

        private static CommunityFixture CreateCommunityFixture()
        {
            Residence residence = CreateCommunityResidence();
            var map = new Mock<IResidenceMapInstance>();
            residence.Map = map.Object;

            var rank = new Mock<IGuildRank>();
            rank
                .Setup(value => value.HasPermission(GuildRankPermission.DecorateCommunity))
                .Returns(true);

            var member = new Mock<IGuildMember>();
            member.SetupGet(value => value.Rank).Returns(rank.Object);

            var community = new Mock<ICommunity>();
            community.SetupGet(value => value.Identity).Returns(new Identity
            {
                RealmId = RealmId,
                Id      = CommunityId
            });
            community.SetupGet(value => value.Residence).Returns(residence);
            community.Setup(value => value.GetMember(CharacterId)).Returns(member.Object);

            var guildManager = new Mock<IGuildManager>();
            guildManager
                .Setup(value => value.GetGuild<ICommunity>(GuildType.Community))
                .Returns(community.Object);

            Mock<IPlayer> player = CreatePlayer(map.Object, CharacterId);
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);
            player.SetupGet(value => value.GuildManager).Returns(guildManager.Object);

            return new CommunityFixture(
                residence,
                player,
                guildManager,
                community,
                member,
                rank);
        }

        private static Mock<IPlayer> CreatePlayer(IResidenceMapInstance map, ulong identityId)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Map).Returns(map);
            player.SetupGet(value => value.Identity).Returns(new Identity
            {
                RealmId = RealmId,
                Id      = identityId
            });
            return player;
        }

        private static Residence CreatePersonalResidence()
        {
            return CreateResidence(new ResidenceModel
            {
                Id             = 1ul,
                OwnerId        = OwnerId,
                PropertyInfoId = PropertyInfoId.Residence,
                Name           = "Personal residence",
                PrivacyLevel   = ResidencePrivacyLevel.Public
            });
        }

        private static Residence CreateCommunityResidence()
        {
            return CreateResidence(new ResidenceModel
            {
                Id             = 2ul,
                GuildOwnerId   = CommunityId,
                PropertyInfoId = PropertyInfoId.Community,
                Name           = "Community residence",
                PrivacyLevel   = ResidencePrivacyLevel.Public
            });
        }

        private static Residence CreateResidence(ResidenceModel model)
        {
            var realmContext = new Mock<IRealmContext>();
            realmContext.SetupGet(value => value.RealmId).Returns(RealmId);
            var residence = new Residence(
                realmContext.Object,
                Mock.Of<IGameTableManager>(),
                Mock.Of<IGlobalResidenceManager>(),
                Mock.Of<IFactory<IPlot>>());
            residence.Initialise(model);
            return residence;
        }

        private sealed record CommunityFixture(
            Residence Residence,
            Mock<IPlayer> Player,
            Mock<IGuildManager> GuildManager,
            Mock<ICommunity> Community,
            Mock<IGuildMember> Member,
            Mock<IGuildRank> Rank);
    }
}
