using NexusForever.Database;
using NexusForever.Game.Persistence;

namespace NexusForever.Game.Tests.Persistence
{
    public class PlayerSavePassTests
    {
        [Fact]
        public async Task TryAcknowledge_AcknowledgesSuccessfulDatabaseWhenOtherDatabaseFails()
        {
            bool authAcknowledged = false;
            SaveCommitAcknowledgement authAcknowledgement = CreateAcknowledgement(() => authAcknowledged = true);
            var characterFailure = new InvalidOperationException("character save failed");
            PlayerSavePass pass = PlayerSavePass.Start(
                () => Task.FromResult(authAcknowledgement),
                () => Task.FromException<SaveCommitAcknowledgement>(characterFailure));

            PlayerSavePassResult result = await pass.Completion;
            bool claimed = pass.TryAcknowledge(result, out List<Exception> exceptions);

            Assert.True(claimed);
            Assert.True(authAcknowledged);
            Assert.Contains(characterFailure, exceptions);

            Assert.False(pass.TryAcknowledge(result, out List<Exception> duplicateExceptions));
            Assert.Empty(duplicateExceptions);
        }

        [Fact]
        public async Task Start_SynchronousFailureStillStartsAndAcknowledgesOtherDatabase()
        {
            bool characterStarted = false;
            bool characterAcknowledged = false;
            var authFailure = new InvalidOperationException("auth save failed synchronously");
            PlayerSavePass pass = PlayerSavePass.Start(
                () => throw authFailure,
                () =>
                {
                    characterStarted = true;
                    return Task.FromResult(CreateAcknowledgement(() => characterAcknowledged = true));
                });

            PlayerSavePassResult result = await pass.Completion;
            pass.TryAcknowledge(result, out List<Exception> exceptions);

            Assert.True(characterStarted);
            Assert.True(characterAcknowledged);
            Assert.Contains(authFailure, exceptions);
        }

        [Fact]
        public void Serialiser_CoalescesConcurrentRequestsIntoOneFollowUpPass()
        {
            var serialiser = new PlayerSaveSerialiser();
            int starts = 0;
            PlayerSavePass StartPass()
            {
                starts++;
                return CreateCompletedPass();
            }

            PlayerSavePass firstPass = serialiser.Request(StartPass);

            Assert.Null(serialiser.Request(StartPass));
            Assert.Null(serialiser.Request(StartPass));

            PlayerSaveTransition firstTransition = serialiser.Complete(firstPass, StartPass);

            Assert.True(firstTransition.IsHandled);
            Assert.NotNull(firstTransition.FollowUp);
            Assert.Equal(2, starts);

            PlayerSaveTransition finalTransition = serialiser.Complete(firstTransition.FollowUp, StartPass);

            Assert.True(finalTransition.IsHandled);
            Assert.Null(finalTransition.FollowUp);
            Assert.Equal(2, starts);
        }

        [Fact]
        public void Serialiser_TakeoverClaimsPendingPassAndIgnoresQueuedCompletion()
        {
            var serialiser = new PlayerSaveSerialiser();
            PlayerSavePass pendingPass = serialiser.Request(CreateCompletedPass);

            Assert.Same(pendingPass, serialiser.BeginTakeover());
            Assert.Null(serialiser.Request(CreateCompletedPass));
            Assert.False(serialiser.Complete(pendingPass, CreateCompletedPass).IsHandled);

            PlayerSavePass finalPass = serialiser.StartTakeoverPass(CreateCompletedPass);
            serialiser.EndTakeover(finalPass);

            Assert.NotNull(serialiser.Request(CreateCompletedPass));
        }

        [Fact]
        public void Serialiser_IgnoresCompletionFromSupersededPass()
        {
            var serialiser = new PlayerSaveSerialiser();
            PlayerSavePass firstPass = serialiser.Request(CreateCompletedPass);
            serialiser.Request(CreateCompletedPass);
            PlayerSaveTransition firstTransition = serialiser.Complete(firstPass, CreateCompletedPass);

            Assert.False(serialiser.Complete(firstPass, CreateCompletedPass).IsHandled);
            Assert.True(serialiser.Complete(firstTransition.FollowUp, CreateCompletedPass).IsHandled);
        }

        private static PlayerSavePass CreateCompletedPass()
        {
            return PlayerSavePass.Start(
                () => Task.FromResult(CreateAcknowledgement(() => { })),
                () => Task.FromResult(CreateAcknowledgement(() => { })));
        }

        private static SaveCommitAcknowledgement CreateAcknowledgement(Action action)
        {
            var commitScope = new SaveCommitScope();
            commitScope.Register(action);
            return commitScope.CreateAcknowledgement();
        }
    }
}
