using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coravel.Scheduling.Schedule;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.Extensions.Logging;

namespace Coravel.Scheduling.HostedService;

public interface ISchedulerService
{
    Task StartAsync(CancellationToken token);
    Task StopAsync(CancellationToken token);
}

public class SchedulerService : ISchedulerService
{
    private const string ScheduledTasksRunningMessage = "Coravel's Scheduling service is attempting to close but there are tasks still running." +
                                                        " App closing (in background) will be prevented until all tasks are completed.";

    private readonly EnsureContinuousSecondTicks _ensureContinuousSecondTicks;
    private readonly ILogger _logger;
    private readonly Scheduler _scheduler;
    private readonly object _tickLockObj = new object();
    private bool _schedulerEnabled = true;
    private Timer _timer;

    public SchedulerService(IScheduler scheduler, ILogger logger)
    {
        _logger = logger;
        _scheduler = scheduler as Scheduler;
        _ensureContinuousSecondTicks = new EnsureContinuousSecondTicks(DateTime.UtcNow);
    }

    public Task StartAsync(CancellationToken token)
    {
        _timer = new Timer(RunSchedulerPerSecondAsync, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken token)
    {
        _schedulerEnabled = false; // Prevents changing the timer from firing scheduled tasks.
        _timer?.Change(Timeout.Infinite, 0);
        _scheduler.CancelAllCancellableTasks();

        // If a previous scheduler execution is still running (due to some long-running scheduled task[s])
        // we don't want to shut down while they are still running.
        if (_scheduler.IsRunning)
        {
            _logger.LogWarning(ScheduledTasksRunningMessage);
        }

        while (_scheduler.IsRunning)
        {
            await Task.Delay(50, token);
        }
    }

    private async void RunSchedulerPerSecondAsync(object state)
    {
        if (!_schedulerEnabled)
        {
            return;
        }

        // This will get any missed ticks that might arise from the Timer triggering a little too late. 
        // If under CPU load or if the Timer is for some reason a little slow, then it's possible to
        // miss a tick - which we want to make sure the scheduler doesn't miss and catches up.
        DateTime now = DateTime.UtcNow;
        DateTime[] ticks;

        lock (_tickLockObj)
        {
            // This class isn't thread-safe.
            ticks = _ensureContinuousSecondTicks.GetTicksBetweenPreviousAndNext(now).ToArray();
            _ensureContinuousSecondTicks.SetNextTick(now);
        }

        if (ticks.Length > 0)
        {
            _logger.LogInformation("Coravel\'s scheduler is behind {TicksLength} ticks and is catching-up to the current tick. Triggered at {S}",
                                ticks.Length,
                                now.ToString("o"));

            foreach (var tick in ticks)
            {
                await _scheduler.RunAtAsync(tick);
            }
        }

        // If we've processed any missed ticks, we also need to explicitly run the current tick.
        await _scheduler.RunAtAsync(now);
    }
}