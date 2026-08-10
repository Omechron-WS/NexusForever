using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Guild;
using NexusForever.Game.Static.Guild;

namespace NexusForever.Game.Tests.Guild
{
    public class GlobalGuildManagerPersistenceTests
    {
        [Fact]
        public async Task ShutdownAsync_MixedDeleteBatchAcknowledgesAndEvictsOnlySuccessfulGuild()
        {
            var successfulCommit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failedCommit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var database = new QueuedCharacterDatabase(
                new ControlledCharacterContext(successfulCommit.Task),
                new ControlledCharacterContext(failedCommit.Task));
            var databaseManager = new Mock<IDatabaseManager>();
            databaseManager.Setup(manager => manager.GetDatabase<CharacterDatabase>()).Returns(database);

            bool successfulAcknowledgement = false;
            bool failedAcknowledgement = false;
            Mock<IGuildBase> successfulGuild = CreateGuild(1ul, "Successful", () => successfulAcknowledgement = true);
            Mock<IGuildBase> failedGuild = CreateGuild(2ul, "Failed", () => failedAcknowledgement = true);
            var guildFactory = new Mock<IGuildFactory>();
            guildFactory.SetupSequence(factory => factory.CreateGuild(
                    It.IsAny<GuildType>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IGuildStandard>()))
                .Returns(successfulGuild.Object)
                .Returns(failedGuild.Object);

            var manager = new GlobalGuildManager(guildFactory.Object, databaseManager.Object);
            manager.RegisterGuild(GuildType.Circle, "Successful", "Leader", "Council", "Member");
            manager.RegisterGuild(GuildType.Circle, "Failed", "Leader", "Council", "Member");
            manager.TrackCharacterGuild(11ul, 1ul);
            manager.TrackCharacterGuild(12ul, 2ul);

            Task shutdown = manager.ShutdownAsync();

            Assert.Same(successfulGuild.Object, manager.GetGuild(1ul));
            Assert.Same(failedGuild.Object, manager.GetGuild(2ul));
            Assert.Same(successfulGuild.Object, Assert.Single(manager.GetCharacterGuilds(11ul)));
            Assert.Same(failedGuild.Object, Assert.Single(manager.GetCharacterGuilds(12ul)));
            Assert.False(successfulAcknowledgement);
            Assert.False(failedAcknowledgement);

            var expected = new InvalidOperationException("save failed");
            successfulCommit.SetResult(1);
            failedCommit.SetException(expected);
            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => shutdown);

            Assert.Contains(expected, exception.InnerExceptions);
            Assert.True(successfulAcknowledgement);
            Assert.False(failedAcknowledgement);
            Assert.Null(manager.GetGuild(1ul));
            Assert.Null(manager.GetGuild(GuildType.Circle, "Successful"));
            Assert.Empty(manager.GetCharacterGuilds(11ul));
            Assert.Same(failedGuild.Object, manager.GetGuild(2ul));
            Assert.Same(failedGuild.Object, manager.GetGuild(GuildType.Circle, "Failed"));
            Assert.Same(failedGuild.Object, Assert.Single(manager.GetCharacterGuilds(12ul)));
        }

        private static Mock<IGuildBase> CreateGuild(ulong id, string name, Action acknowledged)
        {
            var guild = new Mock<IGuildBase>();
            guild.SetupGet(value => value.Id).Returns(id);
            guild.SetupGet(value => value.Type).Returns(GuildType.Circle);
            guild.SetupGet(value => value.Name).Returns(name);
            guild.SetupGet(value => value.PendingCreate).Returns(false);
            guild.SetupGet(value => value.PendingDelete).Returns(true);
            guild.Setup(value => value.Save(It.IsAny<CharacterContext>(), It.IsAny<ISaveCommitScope>()))
                .Callback<CharacterContext, ISaveCommitScope>((_, scope) => scope.Register(acknowledged));
            return guild;
        }

        private sealed class QueuedCharacterDatabase : CharacterDatabase
        {
            private readonly Queue<CharacterContext> contexts;

            public QueuedCharacterDatabase(params CharacterContext[] contexts)
            {
                this.contexts = new Queue<CharacterContext>(contexts);
            }

            protected override CharacterContext CreateContext()
            {
                return contexts.Dequeue();
            }
        }

        private sealed class ControlledCharacterContext : CharacterContext
        {
            private readonly Task<int> saveTask;

            public ControlledCharacterContext(Task<int> saveTask)
                : base(new DbContextOptionsBuilder<CharacterContext>().Options)
            {
                this.saveTask = saveTask;
            }

            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                return saveTask;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
            }
        }
    }
}
