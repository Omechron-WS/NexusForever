using Microsoft.EntityFrameworkCore;
using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Character;

namespace NexusForever.Game.Tests.Persistence
{
    public class DatabaseSaveAcknowledgementTests
    {
        [Fact]
        public async Task CharacterSaveWithAcknowledgement_ReturnsReceiptOnlyAfterSaveSucceeds()
        {
            var saveCompletion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = new ControlledCharacterContext(saveCompletion.Task);
            var database = new ControlledCharacterDatabase(context);
            bool staged = false;
            bool acknowledged = false;

            Task<SaveCommitAcknowledgement> saveTask = database.SaveWithAcknowledgement((_, scope) =>
            {
                staged = true;
                scope.Register(() => acknowledged = true);
            });

            Assert.True(staged);
            Assert.False(saveTask.IsCompleted);
            Assert.False(acknowledged);

            saveCompletion.SetResult(1);
            SaveCommitAcknowledgement acknowledgement = await saveTask;

            Assert.False(acknowledged);
            acknowledgement.Acknowledge();
            Assert.True(acknowledged);
        }

        [Fact]
        public async Task CharacterSaveWithAcknowledgement_SaveFailureReturnsNoReceiptOrAcknowledgement()
        {
            var expected = new InvalidOperationException("save failed");
            var saveCompletion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = new ControlledCharacterContext(saveCompletion.Task);
            var database = new ControlledCharacterDatabase(context);
            bool acknowledged = false;

            Task<SaveCommitAcknowledgement> saveTask = database.SaveWithAcknowledgement((_, scope) =>
                scope.Register(() => acknowledged = true));
            saveCompletion.SetException(expected);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => saveTask);

            Assert.Same(expected, exception);
            Assert.False(acknowledged);
        }

        [Fact]
        public async Task AuthSaveWithAcknowledgement_ReturnsReceiptOnlyAfterSaveSucceeds()
        {
            var saveCompletion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = new ControlledAuthContext(saveCompletion.Task);
            var database = new ControlledAuthDatabase(context);
            bool acknowledged = false;

            Task<SaveCommitAcknowledgement> saveTask = database.SaveWithAcknowledgement((_, scope) =>
                scope.Register(() => acknowledged = true));

            Assert.False(saveTask.IsCompleted);
            saveCompletion.SetResult(1);
            SaveCommitAcknowledgement acknowledgement = await saveTask;

            Assert.False(acknowledged);
            acknowledgement.Acknowledge();
            Assert.True(acknowledged);
        }

        [Fact]
        public async Task CharacterSaveWithAcknowledgement_StagingFailureDoesNotSaveOrAcknowledge()
        {
            var expected = new InvalidOperationException("staging failed");
            var context = new ControlledCharacterContext(Task.FromResult(1));
            var database = new ControlledCharacterDatabase(context);
            bool acknowledged = false;

            Task<SaveCommitAcknowledgement> saveTask = database.SaveWithAcknowledgement((_, scope) =>
            {
                scope.Register(() => acknowledged = true);
                throw expected;
            });

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => saveTask);

            Assert.Same(expected, exception);
            Assert.Equal(0, context.SaveChangesCallCount);
            Assert.False(acknowledged);
        }

        [Fact]
        public async Task AuthSaveWithAcknowledgement_CancellationDoesNotAcknowledge()
        {
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            var context = new ControlledAuthContext(Task.FromCanceled<int>(cancellationSource.Token));
            var database = new ControlledAuthDatabase(context);
            bool acknowledged = false;

            Task<SaveCommitAcknowledgement> saveTask = database.SaveWithAcknowledgement((_, scope) =>
                scope.Register(() => acknowledged = true), cancellationSource.Token);

            OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saveTask);

            Assert.Equal(cancellationSource.Token, exception.CancellationToken);
            Assert.Equal(1, context.SaveChangesCallCount);
            Assert.False(acknowledged);
        }

        private sealed class ControlledCharacterDatabase : CharacterDatabase
        {
            private readonly CharacterContext context;

            public ControlledCharacterDatabase(CharacterContext context)
            {
                this.context = context;
            }

            protected override CharacterContext CreateContext()
            {
                return context;
            }
        }

        private sealed class ControlledAuthDatabase : AuthDatabase
        {
            private readonly AuthContext context;

            public ControlledAuthDatabase(AuthContext context)
            {
                this.context = context;
            }

            protected override AuthContext CreateContext()
            {
                return context;
            }
        }

        private sealed class ControlledCharacterContext : CharacterContext
        {
            public int SaveChangesCallCount { get; private set; }

            private readonly Task<int> saveTask;

            public ControlledCharacterContext(Task<int> saveTask)
                : base(new DbContextOptionsBuilder<CharacterContext>().Options)
            {
                this.saveTask = saveTask;
            }

            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                SaveChangesCallCount++;
                return saveTask;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
            }
        }

        private sealed class ControlledAuthContext : AuthContext
        {
            public int SaveChangesCallCount { get; private set; }

            private readonly Task<int> saveTask;

            public ControlledAuthContext(Task<int> saveTask)
                : base(new DbContextOptionsBuilder<AuthContext>().Options)
            {
                this.saveTask = saveTask;
            }

            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                SaveChangesCallCount++;
                return saveTask;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
            }
        }
    }
}
