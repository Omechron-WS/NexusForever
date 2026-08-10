using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Housing;
using NexusForever.Game.Static.Housing;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Housing
{
    public class GlobalResidenceManagerPersistenceTests
    {
        [Fact]
        public async Task ShutdownAsync_MixedBatchAcknowledgesOnlySuccessfulResidence()
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
            Identity successfulIdentity = new() { RealmId = 1, Id = 1ul };
            Identity failedIdentity = new() { RealmId = 1, Id = 2ul };
            Mock<IResidence> successfulResidence = CreateResidence(successfulIdentity, 11ul, () => successfulAcknowledgement = true);
            Mock<IResidence> failedResidence = CreateResidence(failedIdentity, 12ul, () => failedAcknowledgement = true);
            var residenceFactory = new Mock<IFactory<IResidence>>();
            residenceFactory.SetupSequence(factory => factory.Resolve())
                .Returns(successfulResidence.Object)
                .Returns(failedResidence.Object);

            var manager = new GlobalResidenceManager(
                Mock.Of<ILogger<GlobalResidenceManager>>(),
                Mock.Of<IRealmContext>(),
                databaseManager.Object,
                residenceFactory.Object);
            manager.CreateResidence(CreatePlayer("Successful", 11ul));
            manager.CreateResidence(CreatePlayer("Failed", 12ul));

            Task shutdown = manager.ShutdownAsync();

            Assert.Same(successfulResidence.Object, manager.GetResidence(successfulIdentity));
            Assert.Same(failedResidence.Object, manager.GetResidence(failedIdentity));
            Assert.False(successfulAcknowledgement);
            Assert.False(failedAcknowledgement);

            var expected = new InvalidOperationException("save failed");
            successfulCommit.SetResult(1);
            failedCommit.SetException(expected);
            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => shutdown);

            Assert.Contains(expected, exception.InnerExceptions);
            Assert.True(successfulAcknowledgement);
            Assert.False(failedAcknowledgement);
            Assert.Same(successfulResidence.Object, manager.GetResidence(successfulIdentity));
            Assert.Same(failedResidence.Object, manager.GetResidence(failedIdentity));
        }

        private static Mock<IResidence> CreateResidence(Identity identity, ulong ownerId, Action acknowledged)
        {
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(identity);
            residence.SetupGet(value => value.OwnerIdentity).Returns(new Identity
            {
                RealmId = identity.RealmId,
                Id      = ownerId
            });
            residence.SetupGet(value => value.PrivacyLevel).Returns(ResidencePrivacyLevel.Private);
            residence.Setup(value => value.Save(It.IsAny<CharacterContext>(), It.IsAny<ISaveCommitScope>()))
                .Callback<CharacterContext, ISaveCommitScope>((_, scope) => scope.Register(acknowledged));
            return residence;
        }

        private static IPlayer CreatePlayer(string name, ulong characterId)
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Name).Returns(name);
            player.SetupGet(value => value.Identity).Returns(new Identity
            {
                RealmId = 1,
                Id      = characterId
            });
            return player.Object;
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
