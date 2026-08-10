using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Achievement;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Achievement;
using NexusForever.Game.Static.Achievement;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Tests.Persistence
{
    public sealed class AchievementPersistenceTests
    {
        private const ulong OwnerId = 42ul;
        private const ushort AchievementId = 100;

        [Fact]
        public void Save_LoadedCompletedAchievementIsClean()
        {
            DateTime completed = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            var achievement = new Achievement<CharacterAchievementModel>(CreateInfo(), new CharacterAchievementModel
            {
                Id            = OwnerId,
                AchievementId = AchievementId,
                Data0         = 10u,
                Data1         = 20u,
                DateCompleted = completed
            });

            using TestCharacterContext context = CreateContext();
            achievement.Save(context, new SaveCommitScope());

            Assert.Empty(context.ChangeTracker.Entries<CharacterAchievementModel>());
            Assert.Equal(completed, achievement.DateCompleted);
        }

        [Fact]
        public void ManagerSave_WithoutAcknowledgementRetainsAchievementChanges()
        {
            Achievement<CharacterAchievementModel> achievement = CreateCharacterAchievement();
            achievement.Data0 = 11u;
            achievement.Data1 = 22u;
            achievement.DateCompleted = new DateTime(2026, 8, 10, 13, 0, 0, DateTimeKind.Utc);
            IBaseAchievementManager<CharacterAchievementModel> manager = CreateManager(achievement);

            using (TestCharacterContext context = CreateContext())
                manager.Save(context, new SaveCommitScope());

            using TestCharacterContext retryContext = CreateContext();
            manager.Save(retryContext, new SaveCommitScope());

            CharacterAchievementModel model = Assert.Single(
                retryContext.ChangeTracker.Entries<CharacterAchievementModel>()).Entity;
            Assert.Equal(11u, model.Data0);
            Assert.Equal(22u, model.Data1);
            Assert.Equal(achievement.DateCompleted, model.DateCompleted);
        }

        [Fact]
        public void ManagerSave_SuccessfulAcknowledgementClearsUnchangedChanges()
        {
            Achievement<CharacterAchievementModel> achievement = CreateCharacterAchievement();
            achievement.Data0 = 11u;
            var scope = new SaveCommitScope();
            IBaseAchievementManager<CharacterAchievementModel> manager = CreateManager(achievement);

            using (TestCharacterContext context = CreateContext())
                manager.Save(context, scope);
            scope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext retryContext = CreateContext();
            manager.Save(retryContext, new SaveCommitScope());

            Assert.Empty(retryContext.ChangeTracker.Entries<CharacterAchievementModel>());
        }

        [Fact]
        public void ManagerSave_SameFieldMutationDuringPendingCommitRemainsDirty()
        {
            Achievement<CharacterAchievementModel> achievement = CreateCharacterAchievement();
            achievement.Data0 = 11u;
            var scope = new SaveCommitScope();
            IBaseAchievementManager<CharacterAchievementModel> manager = CreateManager(achievement);

            using (TestCharacterContext context = CreateContext())
                manager.Save(context, scope);

            achievement.Data0 = 12u;
            scope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext retryContext = CreateContext();
            manager.Save(retryContext, new SaveCommitScope());

            CharacterAchievementModel model = Assert.Single(
                retryContext.ChangeTracker.Entries<CharacterAchievementModel>()).Entity;
            Assert.Equal(12u, model.Data0);
        }

        [Fact]
        public void Save_GuildAchievementWithoutAcknowledgementRemainsRetryable()
        {
            var achievement = new Achievement<GuildAchievementModel>(CreateInfo(), new GuildAchievementModel
            {
                Id            = OwnerId,
                AchievementId = AchievementId,
                Data0         = 1u
            });
            achievement.Data0 = 2u;

            using (TestCharacterContext context = CreateContext())
                achievement.Save(context, new SaveCommitScope());

            using TestCharacterContext retryContext = CreateContext();
            achievement.Save(retryContext, new SaveCommitScope());

            GuildAchievementModel model = Assert.Single(
                retryContext.ChangeTracker.Entries<GuildAchievementModel>()).Entity;
            Assert.Equal(2u, model.Data0);
        }

        private static Achievement<CharacterAchievementModel> CreateCharacterAchievement()
        {
            return new Achievement<CharacterAchievementModel>(CreateInfo(), new CharacterAchievementModel
            {
                Id            = OwnerId,
                AchievementId = AchievementId,
                Data0         = 1u,
                Data1         = 2u
            });
        }

        private static IAchievementInfo CreateInfo()
        {
            var info = new Mock<IAchievementInfo>();
            info.SetupGet(value => value.Id).Returns(AchievementId);
            info.SetupGet(value => value.Entry).Returns(new AchievementEntry
            {
                Id = AchievementId
            });
            return info.Object;
        }

        private static IBaseAchievementManager<CharacterAchievementModel> CreateManager(IAchievement achievement)
        {
            var manager = new TestAchievementManager();
            manager.Add(achievement);
            return manager;
        }

        private static TestCharacterContext CreateContext()
        {
            return new TestCharacterContext();
        }

        private sealed class TestAchievementManager : BaseAchievementManager<CharacterAchievementModel>
        {
            protected override ulong OwnerId => AchievementPersistenceTests.OwnerId;

            public void Add(IAchievement achievement)
            {
                achievements.Add(achievement.Id, achievement);
            }

            public override void CheckAchievements(IPlayer target, AchievementType type, uint objectId,
                uint objectIdAlt = 0u, uint count = 1u)
            {
            }

            protected override void SendAchievementUpdate(IEnumerable<IAchievement> updates)
            {
            }
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
