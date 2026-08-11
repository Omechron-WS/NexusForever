using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Achievement;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Achievement;
using NexusForever.Game.Guild;
using NexusForever.Game.Static.Guild;
using NexusForever.GameTable.Model;
using NexusForever.Network.Internal;
using GuildEntity = NexusForever.Game.Guild.Guild;

namespace NexusForever.Game.Tests.Guild
{
    public class GuildPersistenceTests
    {
        [Fact]
        public void GuildRank_SaveWithoutAcknowledgementRetainsDirtyState()
        {
            GuildRank rank = CreateRank();
            rank.Name = "Updated";

            using (CharacterContext context = CreateContext())
                rank.Save(context, new SaveCommitScope(), null);

            using CharacterContext retryContext = CreateContext();
            rank.Save(retryContext, new SaveCommitScope(), null);

            GuildRankModel model = Assert.Single(retryContext.ChangeTracker.Entries<GuildRankModel>()).Entity;
            Assert.Equal("Updated", model.Name);
        }

        [Fact]
        public void GuildRank_SuccessfulAcknowledgementClearsUnchangedDirtyState()
        {
            GuildRank rank = CreateRank();
            rank.Name = "Updated";
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                rank.Save(context, scope, null);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            rank.Save(retryContext, new SaveCommitScope(), null);

            Assert.Empty(retryContext.ChangeTracker.Entries<GuildRankModel>());
        }

        [Fact]
        public void GuildRank_ConcurrentSameFieldMutationSurvivesAcknowledgement()
        {
            GuildRank rank = CreateRank();
            rank.Name = "First";
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                rank.Save(context, scope, null);
            rank.Name = "Second";
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            rank.Save(retryContext, new SaveCommitScope(), null);

            GuildRankModel model = Assert.Single(retryContext.ChangeTracker.Entries<GuildRankModel>()).Entity;
            Assert.Equal("Second", model.Name);
        }

        [Fact]
        public void GuildRank_DeleteCancellationAfterStagingQueuesCompensatingCreate()
        {
            GuildRank rank = CreateRank();
            rank.EnqueueDelete(true);
            bool removed = false;
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                rank.Save(context, scope, () => removed = true);
            rank.EnqueueDelete(false);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            rank.Save(retryContext, new SaveCommitScope(), null);

            Assert.False(removed);
            Assert.True(rank.PendingCreate);
            Assert.Equal(EntityState.Added, Assert.Single(retryContext.ChangeTracker.Entries<GuildRankModel>()).State);
        }

        [Fact]
        public void GuildMember_ConcurrentMutationDuringCreateSurvivesAcknowledgement()
        {
            var guild = new Mock<IGuildBase>();
            guild.SetupGet(value => value.Id).Returns(1ul);
            var rank = new Mock<IGuildRank>();
            rank.SetupGet(value => value.Index).Returns(1);
            var member = new GuildMember(guild.Object, new Identity
            {
                RealmId = 1,
                Id      = 2ul
            }, rank.Object, "First");
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                member.Save(context, scope, null);
            member.Note = "Second";
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            member.Save(retryContext, new SaveCommitScope(), null);

            GuildMemberModel model = Assert.Single(retryContext.ChangeTracker.Entries<GuildMemberModel>()).Entity;
            Assert.Equal("Second", model.Note);
        }

        [Fact]
        public void GuildSave_UnacknowledgedAchievementChangeRemainsRetryable()
        {
            (GuildEntity guild, Achievement<GuildAchievementModel> achievement) = CreateGuildWithAchievement();
            achievement.Data0 = 2u;

            using (CharacterContext context = CreateContext())
                guild.Save(context, new SaveCommitScope());

            using CharacterContext retryContext = CreateContext();
            guild.Save(retryContext, new SaveCommitScope());

            GuildAchievementModel model = Assert.Single(
                retryContext.ChangeTracker.Entries<GuildAchievementModel>()).Entity;
            Assert.Equal(2u, model.Data0);
        }

        [Fact]
        public void GuildSave_AcknowledgedAchievementChangeIsCleared()
        {
            (GuildEntity guild, Achievement<GuildAchievementModel> achievement) = CreateGuildWithAchievement();
            achievement.Data0 = 2u;
            var commitScope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                guild.Save(context, commitScope);

            commitScope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            guild.Save(retryContext, new SaveCommitScope());

            Assert.Empty(retryContext.ChangeTracker.Entries<GuildAchievementModel>());
        }

        [Fact]
        public void GuildSave_AchievementMutationDuringPendingCommitRemainsDirty()
        {
            (GuildEntity guild, Achievement<GuildAchievementModel> achievement) = CreateGuildWithAchievement();
            achievement.Data0 = 2u;
            var commitScope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                guild.Save(context, commitScope);

            achievement.Data0 = 3u;
            commitScope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            guild.Save(retryContext, new SaveCommitScope());

            GuildAchievementModel model = Assert.Single(
                retryContext.ChangeTracker.Entries<GuildAchievementModel>()).Entity;
            Assert.Equal(3u, model.Data0);
        }

        [Fact]
        public void CommunityPrivateFlag_UnacknowledgedSaveRemainsRetryable()
        {
            Community community = CreatePersistedCommunity();
            community.SetCommunityPrivate(true);

            using (CharacterContext context = CreateContext())
            {
                community.Save(context, new SaveCommitScope());
                AssertCommunityFlagUpdate(context, GuildFlag.CommunityPrivate);
            }

            using CharacterContext retryContext = CreateContext();
            community.Save(retryContext, new SaveCommitScope());
            AssertCommunityFlagUpdate(retryContext, GuildFlag.CommunityPrivate);
        }

        [Fact]
        public void CommunityPrivateFlag_AcknowledgedSaveClearsUnchangedDirtyState()
        {
            Community community = CreatePersistedCommunity();
            community.SetCommunityPrivate(true);
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                community.Save(context, scope);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            community.Save(retryContext, new SaveCommitScope());
            Assert.Empty(retryContext.ChangeTracker.Entries<GuildModel>());
        }

        [Fact]
        public void CommunityPrivateFlag_ConcurrentReplaySurvivesAcknowledgement()
        {
            Community community = CreatePersistedCommunity();
            community.SetCommunityPrivate(true);
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                community.Save(context, scope);
            community.SetCommunityPrivate(false);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            community.Save(retryContext, new SaveCommitScope());
            AssertCommunityFlagUpdate(retryContext, GuildFlag.None);
        }

        private static GuildRank CreateRank()
        {
            return new GuildRank(new GuildRankModel
            {
                Id                       = 1ul,
                Index                    = 1,
                Name                     = "Original",
                Permission               = 0u,
                BankWithdrawalPermission = 0ul,
                MoneyWithdrawalLimit     = 0ul,
                RepairLimit              = 0ul
            });
        }

        private static Community CreatePersistedCommunity()
        {
            var community = new Community(
                Mock.Of<IRealmContext>(),
                Mock.Of<IInternalMessagePublisher>(),
                Mock.Of<IGlobalResidenceManager>());
            PropertyInfo identityProperty = typeof(GuildBase).GetProperty(
                nameof(GuildBase.Identity),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            identityProperty.SetValue(community, new Identity
            {
                RealmId = 1,
                Id      = 77ul
            });
            return community;
        }

        private static void AssertCommunityFlagUpdate(
            CharacterContext context,
            GuildFlag expectedFlags)
        {
            var entry = Assert.Single(context.ChangeTracker.Entries<GuildModel>());
            Assert.Equal((uint)expectedFlags, entry.Entity.Flags);
            Assert.True(entry.Property(model => model.Flags).IsModified);
        }

        private static (GuildEntity Guild, Achievement<GuildAchievementModel> Achievement) CreateGuildWithAchievement()
        {
            var guild = new GuildEntity(
                new Mock<IRealmContext>().Object,
                new Mock<IInternalMessagePublisher>().Object);
            var manager = new GuildAchievementManager(guild);
            var info = new Mock<IAchievementInfo>();
            info.SetupGet(value => value.Id).Returns((ushort)100);
            info.SetupGet(value => value.Entry).Returns(new AchievementEntry
            {
                Id = 100u
            });
            var achievement = new Achievement<GuildAchievementModel>(info.Object, new GuildAchievementModel
            {
                Id            = 1ul,
                AchievementId = 100,
                Data0         = 1u
            });

            FieldInfo achievementsField = typeof(BaseAchievementManager<GuildAchievementModel>)
                .GetField("achievements", BindingFlags.Instance | BindingFlags.NonPublic);
            var achievements = (Dictionary<ushort, IAchievement>)achievementsField.GetValue(manager);
            achievements.Add(achievement.Id, achievement);

            typeof(GuildEntity)
                .GetProperty(nameof(GuildEntity.AchievementManager))
                .SetValue(guild, manager);
            return (guild, achievement);
        }

        private static CharacterContext CreateContext()
        {
            return new TestCharacterContext();
        }

        private sealed class TestCharacterContext : CharacterContext
        {
            public TestCharacterContext()
                : base(new DbContextOptionsBuilder<CharacterContext>()
                    .UseMySql(
                        "Server=localhost;Database=nexus_forever_test;User=test;Password=test;",
                        new MySqlServerVersion(new Version(8, 0, 36)))
                    .Options)
            {
            }
        }
    }
}
