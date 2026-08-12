using System.Reflection;
using Moq;
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
    public sealed class CommunityDonateHandlerTests
    {
        private const ulong PlayerId = 100ul;
        private const ulong DonorResidenceId = 200ul;
        private const ulong CommunityId = 300ul;
        private const ulong CommunityResidenceId = 400ul;

        [Fact]
        public void ValidMemberWithoutDecoratePermission_IgnoresPacketResidenceAndTransfersInOrder()
        {
            DonateFixture fixture = CreateFixture(recipientLoaded: true);
            Mock<IDecor> first = fixture.AddDecor(1ul);
            Mock<IDecor> second = fixture.AddDecor(2ul);
            List<string> events = [];
            fixture.CommunityMap
                .Setup(map => map.DecorCopy(fixture.CommunityResidence.Object, first.Object))
                .Callback(() => events.Add("copy-1"));
            fixture.DonorMap
                .Setup(map => map.DecorDelete(fixture.DonorResidence.Object, first.Object))
                .Callback(() => events.Add("delete-1"));
            fixture.CommunityMap
                .Setup(map => map.DecorCopy(fixture.CommunityResidence.Object, second.Object))
                .Callback(() => events.Add("copy-2"));
            fixture.DonorMap
                .Setup(map => map.DecorDelete(fixture.DonorResidence.Object, second.Object))
                .Callback(() => events.Add("delete-2"));
            ClientHousingCommunityDonate message = CreateMessage(
                CreateDecorInfo(1ul, realmId: 77, residenceId: 700ul),
                CreateDecorInfo(2ul, realmId: 88, residenceId: 800ul));

            Handle(fixture, message);

            Assert.Equal(["copy-1", "delete-1", "copy-2", "delete-2"], events);
            fixture.Rank.Verify(
                rank => rank.HasPermission(It.IsAny<GuildRankPermission>()),
                Times.Never);
            fixture.CommunityMap.Verify(
                map => map.DecorCopy(fixture.CommunityResidence.Object, It.IsAny<IDecor>()),
                Times.Exactly(2));
            fixture.DonorMap.Verify(
                map => map.DecorDelete(fixture.DonorResidence.Object, It.IsAny<IDecor>()),
                Times.Exactly(2));
        }

        [Fact]
        public void ValidMember_UnloadedCommunityResidenceUsesDirectCopy()
        {
            DonateFixture fixture = CreateFixture(recipientLoaded: false);
            Mock<IDecor> decor = fixture.AddDecor(1ul);
            List<string> events = [];
            fixture.CommunityResidence
                .Setup(residence => residence.DecorCopy(decor.Object))
                .Callback(() => events.Add("copy"));
            fixture.DonorMap
                .Setup(map => map.DecorDelete(fixture.DonorResidence.Object, decor.Object))
                .Callback(() => events.Add("delete"));

            Handle(fixture, CreateMessage(CreateDecorInfo(1ul)));

            Assert.Equal(["copy", "delete"], events);
            fixture.CommunityResidence.Verify(
                residence => residence.DecorCopy(decor.Object),
                Times.Once);
            fixture.DonorMap.Verify(
                map => map.DecorDelete(fixture.DonorResidence.Object, decor.Object),
                Times.Once);
        }

        [Fact]
        public void DuplicateDecorId_IsRejectedBeforeAnyMutationAndResolvedOnce()
        {
            DonateFixture fixture = CreateFixture();
            fixture.AddDecor(1ul);

            Assert.Throws<InvalidPacketValueException>(() => Handle(
                fixture,
                CreateMessage(CreateDecorInfo(1ul), CreateDecorInfo(1ul))));

            fixture.DonorResidence.Verify(residence => residence.GetDecor(1ul), Times.Once);
            AssertNoMutation(fixture);
        }

        [Theory]
        [InlineData(DecorFailure.Missing)]
        [InlineData(DecorFailure.NotCrated)]
        [InlineData(DecorFailure.PendingDelete)]
        [InlineData(DecorFailure.WrongResidenceReference)]
        [InlineData(DecorFailure.WrongResidenceIdentity)]
        [InlineData(DecorFailure.WrongStoredId)]
        public void InvalidStoredDecor_IsRejectedBeforeAnyMutation(DecorFailure failure)
        {
            DonateFixture fixture = CreateFixture();
            ConfigureDecorFailure(fixture, 1ul, failure);

            Assert.Throws<InvalidPacketValueException>(() =>
                Handle(fixture, CreateMessage(CreateDecorInfo(1ul))));

            AssertNoMutation(fixture);
        }

        [Fact]
        public void LaterInvalidDecor_PreventsEarlierTransfer()
        {
            DonateFixture fixture = CreateFixture();
            fixture.AddDecor(1ul);

            Assert.Throws<InvalidPacketValueException>(() => Handle(
                fixture,
                CreateMessage(CreateDecorInfo(1ul), CreateDecorInfo(2ul))));

            AssertNoMutation(fixture);
        }

        [Theory]
        [InlineData(AuthorityFailure.NonResidenceMap)]
        [InlineData(AuthorityFailure.WrongDonorOwner)]
        [InlineData(AuthorityFailure.WrongDonorType)]
        [InlineData(AuthorityFailure.MissingDonorMap)]
        [InlineData(AuthorityFailure.DifferentDonorMap)]
        [InlineData(AuthorityFailure.MissingCommunity)]
        [InlineData(AuthorityFailure.PendingCommunity)]
        [InlineData(AuthorityFailure.MissingCommunityResidence)]
        [InlineData(AuthorityFailure.WrongCommunityResidenceType)]
        [InlineData(AuthorityFailure.WrongCommunityOwner)]
        [InlineData(AuthorityFailure.ReplacedCommunityResidence)]
        [InlineData(AuthorityFailure.MissingMember)]
        [InlineData(AuthorityFailure.MissingRank)]
        public void InvalidAuthority_IsRejectedBeforeDecorResolutionOrMutation(AuthorityFailure failure)
        {
            DonateFixture fixture = CreateFixture();
            fixture.AddDecor(1ul);
            ApplyAuthorityFailure(fixture, failure);

            Assert.Throws<InvalidPacketValueException>(() =>
                Handle(fixture, CreateMessage(CreateDecorInfo(1ul))));

            fixture.DonorResidence.Verify(
                residence => residence.GetDecor(It.IsAny<ulong>()),
                Times.Never);
            AssertNoMutation(fixture);
        }

        private static DonateFixture CreateFixture(bool recipientLoaded = true)
        {
            var playerIdentity = new GameIdentity { RealmId = 1, Id = PlayerId };
            var donorIdentity = new GameIdentity { RealmId = 1, Id = DonorResidenceId };
            var communityIdentity = new GameIdentity { RealmId = 1, Id = CommunityId };
            var communityResidenceIdentity = new GameIdentity
            {
                RealmId = 1,
                Id      = CommunityResidenceId
            };
            var donorMap = new Mock<IResidenceMapInstance>();
            var communityMap = new Mock<IResidenceMapInstance>();

            var donorResidence = new Mock<IResidence>();
            donorResidence.SetupGet(residence => residence.Identity).Returns(donorIdentity);
            donorResidence.SetupGet(residence => residence.Type).Returns(ResidenceType.Residence);
            donorResidence.SetupGet(residence => residence.OwnerIdentity).Returns(playerIdentity);
            donorResidence.SetupGet(residence => residence.Map).Returns(donorMap.Object);

            var communityResidence = new Mock<IResidence>();
            communityResidence.SetupGet(residence => residence.Identity).Returns(communityResidenceIdentity);
            communityResidence.SetupGet(residence => residence.Type).Returns(ResidenceType.Community);
            communityResidence.SetupGet(residence => residence.GuildOwnerIdentity).Returns(communityIdentity);
            communityResidence.SetupGet(residence => residence.Map).Returns(
                recipientLoaded ? communityMap.Object : null);

            var rank = new Mock<IGuildRank>();
            rank.SetupGet(value => value.Permissions).Returns(GuildRankPermission.None);
            var member = new Mock<IGuildMember>();
            member.SetupGet(value => value.Rank).Returns(rank.Object);
            var community = new Mock<ICommunity>();
            community.SetupGet(value => value.Identity).Returns(communityIdentity);
            community.SetupGet(value => value.PendingDelete).Returns(false);
            community.SetupGet(value => value.Residence).Returns(communityResidence.Object);
            community.Setup(value => value.GetMember(PlayerId)).Returns(member.Object);

            var guildManager = new Mock<IGuildManager>();
            guildManager
                .Setup(manager => manager.GetGuild<ICommunity>(GuildType.Community))
                .Returns(community.Object);
            var residenceManager = new Mock<IResidenceManager>();
            residenceManager.SetupGet(manager => manager.Residence).Returns(donorResidence.Object);
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Identity).Returns(playerIdentity);
            player.SetupGet(value => value.CharacterId).Returns(PlayerId);
            player.SetupGet(value => value.Map).Returns(donorMap.Object);
            player.SetupGet(value => value.GuildManager).Returns(guildManager.Object);
            player.SetupGet(value => value.ResidenceManager).Returns(residenceManager.Object);
            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            return new DonateFixture(
                new ClientHousingCommunityDonateHandler(),
                session,
                player,
                residenceManager,
                donorResidence,
                donorMap,
                guildManager,
                community,
                member,
                rank,
                communityResidence,
                communityMap,
                playerIdentity,
                donorIdentity,
                communityIdentity);
        }

        private static void ConfigureDecorFailure(
            DonateFixture fixture,
            ulong requestedId,
            DecorFailure failure)
        {
            if (failure == DecorFailure.Missing)
                return;

            Mock<IDecor> decor = fixture.AddDecor(requestedId);
            switch (failure)
            {
                case DecorFailure.NotCrated:
                    decor.SetupGet(value => value.Type).Returns(DecorType.FreePlace);
                    break;
                case DecorFailure.PendingDelete:
                    decor.SetupGet(value => value.PendingDelete).Returns(true);
                    break;
                case DecorFailure.WrongResidenceReference:
                    decor.SetupGet(value => value.Residence).Returns(Mock.Of<IResidence>());
                    break;
                case DecorFailure.WrongResidenceIdentity:
                    decor.SetupGet(value => value.ResidenceIdentity).Returns(
                        new GameIdentity { RealmId = 1, Id = DonorResidenceId + 1ul });
                    break;
                case DecorFailure.WrongStoredId:
                    decor.SetupGet(value => value.DecorId).Returns(requestedId + 1ul);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }

        private static void ApplyAuthorityFailure(
            DonateFixture fixture,
            AuthorityFailure failure)
        {
            switch (failure)
            {
                case AuthorityFailure.NonResidenceMap:
                    fixture.Player.SetupGet(value => value.Map).Returns(Mock.Of<IBaseMap>());
                    break;
                case AuthorityFailure.WrongDonorOwner:
                    fixture.DonorResidence.SetupGet(value => value.OwnerIdentity).Returns(
                        new GameIdentity { RealmId = 1, Id = PlayerId + 1ul });
                    break;
                case AuthorityFailure.WrongDonorType:
                    fixture.DonorResidence.SetupGet(value => value.Type).Returns(ResidenceType.Community);
                    break;
                case AuthorityFailure.MissingDonorMap:
                    fixture.DonorResidence.SetupGet(value => value.Map).Returns((IResidenceMapInstance)null);
                    break;
                case AuthorityFailure.DifferentDonorMap:
                    fixture.DonorResidence.SetupGet(value => value.Map).Returns(Mock.Of<IResidenceMapInstance>());
                    break;
                case AuthorityFailure.MissingCommunity:
                    fixture.GuildManager
                        .Setup(manager => manager.GetGuild<ICommunity>(GuildType.Community))
                        .Returns((ICommunity)null);
                    break;
                case AuthorityFailure.PendingCommunity:
                    fixture.Community.SetupGet(value => value.PendingDelete).Returns(true);
                    break;
                case AuthorityFailure.MissingCommunityResidence:
                    fixture.Community.SetupGet(value => value.Residence).Returns((IResidence)null);
                    break;
                case AuthorityFailure.WrongCommunityResidenceType:
                    fixture.CommunityResidence.SetupGet(value => value.Type).Returns(ResidenceType.Residence);
                    break;
                case AuthorityFailure.WrongCommunityOwner:
                    fixture.CommunityResidence.SetupGet(value => value.GuildOwnerIdentity).Returns(
                        new GameIdentity { RealmId = 1, Id = CommunityId + 1ul });
                    break;
                case AuthorityFailure.ReplacedCommunityResidence:
                    fixture.Community
                        .SetupSequence(value => value.Residence)
                        .Returns(fixture.CommunityResidence.Object)
                        .Returns(Mock.Of<IResidence>());
                    break;
                case AuthorityFailure.MissingMember:
                    fixture.Community.Setup(value => value.GetMember(PlayerId)).Returns((IGuildMember)null);
                    break;
                case AuthorityFailure.MissingRank:
                    fixture.Member.SetupGet(value => value.Rank).Returns((IGuildRank)null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }

        private static void AssertNoMutation(DonateFixture fixture)
        {
            fixture.CommunityMap.Verify(
                map => map.DecorCopy(It.IsAny<IResidence>(), It.IsAny<IDecor>()),
                Times.Never);
            fixture.CommunityResidence.Verify(
                residence => residence.DecorCopy(It.IsAny<IDecor>()),
                Times.Never);
            fixture.DonorMap.Verify(
                map => map.DecorDelete(It.IsAny<IResidence>(), It.IsAny<IDecor>()),
                Times.Never);
            fixture.DonorResidence.Verify(
                residence => residence.DecorRemove(It.IsAny<IDecor>()),
                Times.Never);
        }

        private static void Handle(
            DonateFixture fixture,
            ClientHousingCommunityDonate message)
        {
            fixture.Handler.HandleMessage(fixture.Session.Object, message);
        }

        private static ClientHousingCommunityDonate CreateMessage(params DecorInfo[] decor)
        {
            var message = new ClientHousingCommunityDonate();
            message.Decor.AddRange(decor);
            return message;
        }

        private static DecorInfo CreateDecorInfo(
            ulong decorId,
            ushort realmId = 99,
            ulong residenceId = 999ul)
        {
            var info = new DecorInfo();
            info.TargetResidence.RealmId = realmId;
            info.TargetResidence.Id = residenceId;
            PropertyInfo property = typeof(DecorInfo).GetProperty(
                nameof(DecorInfo.DecorId),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(info, decorId);
            return info;
        }

        public enum DecorFailure
        {
            Missing,
            NotCrated,
            PendingDelete,
            WrongResidenceReference,
            WrongResidenceIdentity,
            WrongStoredId
        }

        public enum AuthorityFailure
        {
            NonResidenceMap,
            WrongDonorOwner,
            WrongDonorType,
            MissingDonorMap,
            DifferentDonorMap,
            MissingCommunity,
            PendingCommunity,
            MissingCommunityResidence,
            WrongCommunityResidenceType,
            WrongCommunityOwner,
            ReplacedCommunityResidence,
            MissingMember,
            MissingRank
        }

        private sealed record DonateFixture(
            ClientHousingCommunityDonateHandler Handler,
            Mock<IWorldSession> Session,
            Mock<IPlayer> Player,
            Mock<IResidenceManager> ResidenceManager,
            Mock<IResidence> DonorResidence,
            Mock<IResidenceMapInstance> DonorMap,
            Mock<IGuildManager> GuildManager,
            Mock<ICommunity> Community,
            Mock<IGuildMember> Member,
            Mock<IGuildRank> Rank,
            Mock<IResidence> CommunityResidence,
            Mock<IResidenceMapInstance> CommunityMap,
            GameIdentity PlayerIdentity,
            GameIdentity DonorIdentity,
            GameIdentity CommunityIdentity)
        {
            public Mock<IDecor> AddDecor(ulong decorId)
            {
                var decor = new Mock<IDecor>();
                decor.SetupGet(value => value.DecorId).Returns(decorId);
                decor.SetupGet(value => value.Residence).Returns(DonorResidence.Object);
                decor.SetupGet(value => value.ResidenceIdentity).Returns(DonorIdentity);
                decor.SetupGet(value => value.Type).Returns(DecorType.Crate);
                decor.SetupGet(value => value.PendingDelete).Returns(false);
                DonorResidence.Setup(residence => residence.GetDecor(decorId)).Returns(decor.Object);
                return decor;
            }
        }
    }
}
