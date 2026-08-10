using System;
using System.Threading.Tasks;
using NLog;

namespace NexusForever.Shared
{
    public static class TaskExtensions
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Observe a task that intentionally continues outside the caller's control flow.
        /// </summary>
        /// <remarks>
        /// Failures are passed to <paramref name="onException"/> or logged when no callback is supplied.
        /// </remarks>
        public static async void FireAndForgetAsync(this Task task, Action<Exception> onException = null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (onException != null)
                    onException.Invoke(exception);
                else
                    log.Error(exception, "An asynchronous operation failed after its caller continued.");
            }
        }
    }
}
