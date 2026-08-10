using System;

namespace NexusForever.Database
{
    /// <summary>
    /// Registers actions that acknowledge in-memory state after a database commit succeeds.
    /// </summary>
    public interface ISaveCommitScope
    {
        /// <summary>
        /// Register an action to execute after the staged database changes commit successfully.
        /// </summary>
        /// <param name="action">Action that acknowledges committed in-memory state.</param>
        void Register(Action action);
    }
}
