using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coravel.Scheduling.Schedule.Event;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.Extensions.Logging;

namespace Coravel.Scheduling.Schedule
{
    internal sealed class Scheduler : IScheduler
    {
        private const int EventLockTimeout24Hours = 1440;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly IMutex _mutex;
        private readonly ConcurrentDictionary<string, ScheduledTask> _tasks;
        private Action<Exception> _errorHandler;
        private bool _isFirstTick = true;
        private ILogger<IScheduler> _logger;
        private int _schedulerIterationsActiveCount;

        public Scheduler(IMutex mutex, IEnumerable<IScheduledEvent> scheduledEvents)
        {
            _mutex = mutex;
            _cancellationTokenSource = new CancellationTokenSource();
            _tasks = new ConcurrentDictionary<string, ScheduledTask>();

            foreach (IScheduledEvent scheduledEvent in scheduledEvents)
            {
                _tasks.TryAdd(scheduledEvent.OverlappingUniqueIdentifier, new ScheduledTask("default", scheduledEvent));
            }
        }

        public bool IsRunning => _schedulerIterationsActiveCount > 0;

        public void CancelAllCancellableTasks()
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }
        }

        public async Task RunAtAsync(DateTime utcDate)
        {
            Interlocked.Increment(ref _schedulerIterationsActiveCount);
            bool isFirstTick = _isFirstTick;
            _isFirstTick = false;
            await RunWorkersAt(utcDate, isFirstTick);
            Interlocked.Decrement(ref _schedulerIterationsActiveCount);
        }

        public IScheduler OnError(Action<Exception> onError)
        {
            _errorHandler = onError;
            return this;
        }

        public IScheduler LogScheduledTaskProgress(ILogger<IScheduler> logger)
        {
            _logger = logger;
            return this;
        }

        public bool TryUnschedule(string uniqueIdentifier)
        {
            (string guid, ScheduledTask value) = _tasks.FirstOrDefault(scheduledEvent =>
                                                                           scheduledEvent.Value.ScheduledEvent.OverlappingUniqueIdentifier ==
                                                                           uniqueIdentifier);

            return value == null ||
                   // Nothing to remove - was successful.
                   _tasks.TryRemove(guid, out var dummy); // If failed, caller can try again etc.
        }

        private async Task InvokeEventWithLoggerScope(IScheduledEvent scheduledEvent)
        {
            var eventInvocableTypeName = scheduledEvent.InvocableType?.Name;

            using (_logger != null && eventInvocableTypeName != null ? _logger.BeginScope($"Invocable Type : {eventInvocableTypeName}") : null)
            {
                await InvokeEvent(scheduledEvent);
            }
        }

        private async Task InvokeEvent(IScheduledEvent scheduledEvent)
        {
            try
            {
                async Task Invoke()
                {
                    _logger?.LogDebug("Scheduled task started...");
                    await scheduledEvent.InvokeScheduledEvent(_cancellationTokenSource.Token);
                    _logger?.LogDebug("Scheduled task finished...");
                }

                if (scheduledEvent.ShouldPreventOverlapping)
                {
                    if (_mutex.TryGetLock(scheduledEvent.OverlappingUniqueIdentifier, EventLockTimeout24Hours))
                    {
                        try
                        {
                            await Invoke();
                        }
                        finally
                        {
                            _mutex.Release(scheduledEvent.OverlappingUniqueIdentifier);
                        }
                    }
                }
                else
                {
                    await Invoke();
                }
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "A scheduled task threw an Exception: ");
                _errorHandler?.Invoke(e);
            }
        }

        /// <summary>
        ///     This will grab all the scheduled tasks and combine each task into its assigned "worker".
        ///     Each worker runs on its own thread and will process its assigned scheduled tasks asynchronously.
        ///     This method return a list of active tasks (one per worker - which needs to be awaited).
        /// </summary>
        /// <param name="utcDate"></param>
        /// <param name="isFirstTick"></param>
        /// <returns></returns>
        private async Task RunWorkersAt(DateTime utcDate, bool isFirstTick)
        {
            // Grab all the scheduled tasks so we can re-arrange them etc.
            List<ScheduledTask> scheduledWorkers = new List<ScheduledTask>();

            foreach (var keyValue in _tasks)
            {
                bool timerIsAtMinute = utcDate.Second == 0;
                bool taskIsSecondsBased = !keyValue.Value.ScheduledEvent.IsScheduledCronBasedTask;
                bool forceRunAtStart = isFirstTick && keyValue.Value.ScheduledEvent.ShouldRunOnceAtStart;
                bool canRunBasedOnTimeMarker = taskIsSecondsBased || timerIsAtMinute;

                // If this task is scheduled as a cron based task (should only be checked if due per min)
                // but the time is not at the minute mark, we won't include those tasks to be checked if due.
                // The second based schedules are always checked.

                if (canRunBasedOnTimeMarker && keyValue.Value.ScheduledEvent.IsDue(utcDate))
                {
                    scheduledWorkers.Add(keyValue.Value);
                }
                else if (forceRunAtStart)
                {
                    scheduledWorkers.Add(keyValue.Value);
                }
            }

            // We want each "worker" (indicated by the "WorkerName" prop) to run on its own thread.
            // So we'll group all the "due" scheduled events (the actual work the user wants to perform) into
            // buckets for each "worker".
            IEnumerable<IGrouping<string, ScheduledTask>> groupedScheduledEvents = scheduledWorkers.GroupBy(worker => worker.WorkerName);

            IEnumerable<Task> activeTasks = groupedScheduledEvents.Select(workerWithTasks =>
            {
                // Each group represents the "worker" for that group of scheduled events.
                // Running them on a separate thread means we can segment longer running tasks
                // onto their own thread, or maybe more cpu intensive operations onto an isolated thread, etc.
                return Task.Run(async () =>
                {
                    foreach (ScheduledTask workerTask in workerWithTasks)
                    {
                        IScheduledEvent scheduledEvent = workerTask.ScheduledEvent;
                        await InvokeEventWithLoggerScope(scheduledEvent);
                    }
                });
            });

            await Task.WhenAll(activeTasks);
        }

        private class ScheduledTask
        {
            public ScheduledTask(string workerName, IScheduledEvent scheduledEvent)
            {
                WorkerName = workerName;
                ScheduledEvent = scheduledEvent;
            }

            public IScheduledEvent ScheduledEvent { get; }
            public string WorkerName { get; }
        }
    }
}