using NexusForever.Database;

namespace NexusForever.Game.Persistence
{
    /// <summary>
    /// Runs independent authentication and character database saves and acknowledges each successful commit once.
    /// </summary>
    internal sealed class PlayerSavePass
    {
        private int isAcknowledged;

        public Task<PlayerSavePassResult> Completion { get; }

        private PlayerSavePass(Task<PlayerSavePassResult> completion)
        {
            Completion = completion;
        }

        /// <summary>
        /// Start both independent database save attempts even when either factory fails synchronously.
        /// </summary>
        public static PlayerSavePass Start(
            Func<Task<SaveCommitAcknowledgement>> startAuthSave,
            Func<Task<SaveCommitAcknowledgement>> startCharacterSave)
        {
            ArgumentNullException.ThrowIfNull(startAuthSave);
            ArgumentNullException.ThrowIfNull(startCharacterSave);

            Task<DatabaseSaveAttempt> authSave = StartSaveAttempt(startAuthSave);
            Task<DatabaseSaveAttempt> characterSave = StartSaveAttempt(startCharacterSave);
            return new PlayerSavePass(CompleteAsync(authSave, characterSave));
        }

        /// <summary>
        /// Acknowledge successful commits once and return every database or acknowledgement failure.
        /// </summary>
        public bool TryAcknowledge(PlayerSavePassResult result, out List<Exception> exceptions)
        {
            ArgumentNullException.ThrowIfNull(result);

            exceptions = [];
            if (Interlocked.Exchange(ref isAcknowledged, 1) != 0)
                return false;

            ProcessAttempt(result.Auth, exceptions);
            ProcessAttempt(result.Character, exceptions);
            return true;
        }

        private static Task<DatabaseSaveAttempt> StartSaveAttempt(Func<Task<SaveCommitAcknowledgement>> startSave)
        {
            try
            {
                return ObserveSaveAttempt(startSave.Invoke());
            }
            catch (Exception exception)
            {
                return Task.FromResult(new DatabaseSaveAttempt
                {
                    Exception = exception
                });
            }
        }

        private static async Task<DatabaseSaveAttempt> ObserveSaveAttempt(Task<SaveCommitAcknowledgement> saveTask)
        {
            try
            {
                return new DatabaseSaveAttempt
                {
                    Acknowledgement = await saveTask
                };
            }
            catch (Exception exception)
            {
                return new DatabaseSaveAttempt
                {
                    Exception = exception
                };
            }
        }

        private static async Task<PlayerSavePassResult> CompleteAsync(
            Task<DatabaseSaveAttempt> authSave,
            Task<DatabaseSaveAttempt> characterSave)
        {
            await Task.WhenAll(authSave, characterSave);
            return new PlayerSavePassResult
            {
                Auth      = await authSave,
                Character = await characterSave
            };
        }

        private static void ProcessAttempt(DatabaseSaveAttempt attempt, ICollection<Exception> exceptions)
        {
            if (attempt.Exception != null)
            {
                exceptions.Add(attempt.Exception);
                return;
            }

            try
            {
                attempt.Acknowledgement?.Acknowledge();
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }
    }

    /// <summary>
    /// Results from independent authentication and character database save attempts.
    /// </summary>
    internal sealed class PlayerSavePassResult
    {
        public DatabaseSaveAttempt Auth { get; init; }
        public DatabaseSaveAttempt Character { get; init; }
    }

    /// <summary>
    /// Result from one database save attempt.
    /// </summary>
    internal sealed class DatabaseSaveAttempt
    {
        public SaveCommitAcknowledgement Acknowledgement { get; init; }
        public Exception Exception { get; init; }
    }
}
