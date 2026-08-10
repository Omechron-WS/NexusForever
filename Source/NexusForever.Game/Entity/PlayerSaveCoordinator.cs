using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Game.Entity
{
    /// <summary>
    /// Coordinates a set of player saves while preserving every independent save attempt.
    /// </summary>
    public static class PlayerSaveCoordinator
    {
        /// <summary>
        /// Start and await every supplied player save, then report all observed failures together.
        /// </summary>
        public static async Task SaveAsync(IEnumerable<IPlayer> players, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(players);

            var tasks = new List<Task>();
            var exceptions = new List<Exception>();
            foreach (IPlayer player in players)
            {
                if (player == null)
                    continue;

                try
                {
                    Task task = player.SaveDirectAsync(cancellationToken);
                    tasks.Add(task ?? Task.FromException(new InvalidOperationException("Player save returned no task.")));
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }
            }

            Task allTasks = Task.WhenAll(tasks);
            OperationCanceledException cancellationException = null;
            try
            {
                await allTasks;
            }
            catch (OperationCanceledException exception)
            {
                cancellationException = exception;
            }
            catch when (allTasks.Exception != null)
            {
                exceptions.AddRange(allTasks.Exception.Flatten().InnerExceptions);
            }

            if (exceptions.Count != 0)
            {
                if (cancellationException != null)
                    exceptions.Add(cancellationException);

                throw new AggregateException("One or more player saves failed.", exceptions);
            }

            if (cancellationException != null)
                throw cancellationException;

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
