using System;
using System.Collections.Generic;
using System.Threading;

namespace NexusForever.Database
{
    /// <summary>
    /// Executes ordered actions that acknowledge an already committed database save.
    /// </summary>
    public sealed class SaveCommitAcknowledgement
    {
        private readonly IReadOnlyList<Action> actions;
        private int isAcknowledged;

        internal SaveCommitAcknowledgement(IReadOnlyList<Action> actions)
        {
            this.actions = actions;
        }

        /// <summary>
        /// Execute every registered acknowledgement action exactly once.
        /// </summary>
        /// <exception cref="AggregateException">One or more acknowledgement actions failed.</exception>
        public void Acknowledge()
        {
            if (Interlocked.Exchange(ref isAcknowledged, 1) != 0)
                return;

            List<Exception> exceptions = null;
            foreach (Action action in actions)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception exception)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(exception);
                }
            }

            if (exceptions != null)
                throw new AggregateException("One or more save acknowledgement actions failed.", exceptions);
        }
    }
}
