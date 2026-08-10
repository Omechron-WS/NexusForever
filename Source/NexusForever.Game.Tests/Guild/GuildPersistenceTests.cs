using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Guild;

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
