using System;
using System.Collections.Generic;

namespace NexusForever.Database
{
    /// <summary>
    /// Collects actions that acknowledge in-memory state after a database commit succeeds.
    /// </summary>
    public sealed class SaveCommitScope : ISaveCommitScope
    {
        private readonly object syncRoot = new();
        private readonly List<Action> actions = new();
        private bool isCompleted;

        /// <summary>
        /// Register an action to execute after the staged database changes commit successfully.
        /// </summary>
        /// <param name="action">Action that acknowledges committed in-memory state.</param>
        public void Register(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            lock (syncRoot)
            {
                if (isCompleted)
                    throw new InvalidOperationException("The save commit scope has already been completed.");

                actions.Add(action);
            }
        }

        /// <summary>
        /// Complete this scope and create a one-shot acknowledgement containing its registered actions.
        /// </summary>
        /// <returns>A one-shot acknowledgement that preserves action registration order.</returns>
        internal SaveCommitAcknowledgement CreateAcknowledgement()
        {
            lock (syncRoot)
            {
                if (isCompleted)
                    throw new InvalidOperationException("The save commit scope has already been completed.");

                isCompleted = true;
                return new SaveCommitAcknowledgement(actions.ToArray());
            }
        }
    }
}
