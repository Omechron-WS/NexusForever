using System;
using System.Threading.Tasks;

namespace NexusForever.Shared.Game.Events
{
    /// <summary>
    /// An <see cref="IEvent"/> that invokes a callback after a <see cref="Task"/> completes.
    /// </summary>
    public class TaskEvent : IEvent
    {
        private readonly Task task;
        private readonly Action callback;
        private readonly Action<Exception> exceptionCallback;

        /// <summary>
        /// Initialise a task-backed event with success and optional failure callbacks.
        /// </summary>
        public TaskEvent(Task task, Action callback, Action<Exception> exceptionCallback = null)
        {
            this.task              = task;
            this.callback          = callback;
            this.exceptionCallback = exceptionCallback;
        }

        /// <summary>
        /// Returns if <see cref="TaskEvent"/> can be executed.
        /// </summary>
        public bool CanExecute()
        {
            return task.IsCompleted;
        }

        /// <summary>
        /// Execute the success callback or pass any failure to the configured failure callback.
        /// </summary>
        public void Execute()
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exceptionCallback != null)
            {
                exceptionCallback.Invoke(exception);
                return;
            }

            callback.Invoke();
        }
    }
}
