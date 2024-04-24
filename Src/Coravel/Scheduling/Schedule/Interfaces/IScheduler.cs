using System;
using System.Threading.Tasks;
using Coravel.Invocable;
using Microsoft.Extensions.Logging;

namespace Coravel.Scheduling.Schedule.Interfaces
{
    /// <summary>
    ///     Provides methods for scheduling tasks using Coravel.
    /// </summary>
    public interface IScheduler
    {
        /// <summary>
        ///     Global error handler invoked whenever a scheduled task throws an exception.
        /// </summary>
        /// <param name="onError">Error handler to invoke on error.</param>
        /// <returns></returns>
        IScheduler OnError(Action<Exception> onError);

        /// <summary>
        ///     Log the progress of scheduled tasks.
        /// </summary>
        /// <param name="logger"></param>
        /// <returns></returns>
        IScheduler LogScheduledTaskProgress(ILogger<IScheduler> logger);
    }
}