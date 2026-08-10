using NexusForever.Database;

namespace NexusForever.Game.Persistence
{
    /// <summary>
    /// Executes acknowledgements immediately for legacy save entry points that do not expose commit completion.
    /// </summary>
    internal sealed class ImmediateSaveCommitScope : ISaveCommitScope
    {
        public static ImmediateSaveCommitScope Instance { get; } = new();

        private ImmediateSaveCommitScope()
        {
        }

        /// <summary>
        /// Execute the supplied acknowledgement immediately.
        /// </summary>
        public void Register(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action.Invoke();
        }
    }
}
