using System;
using System.Threading.Tasks;

namespace NexusForever.Shared.Game.Events
{
    public class TaskGenericEvent<T> : IEvent
    {
        private readonly Task<T> task;
        private readonly Action<T> callback;
        private readonly Action<Exception> exceptionCallback;

        /// <summary>
        /// Initialise a task-backed event with success and optional failure callbacks.
        /// </summary>
        public TaskGenericEvent(Task<T> task, Action<T> callback, Action<Exception> exceptionCallback = null)
        {
            this.task              = task;
            this.callback          = callback;
            this.exceptionCallback = exceptionCallback;
        }

        /// <summary>
        /// Return whether the backing task has completed.
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
                callback.Invoke(task.GetAwaiter().GetResult());
            }
            catch (Exception exception) when (exceptionCallback != null)
            {
                exceptionCallback.Invoke(exception);
            }
        }
    }
}
