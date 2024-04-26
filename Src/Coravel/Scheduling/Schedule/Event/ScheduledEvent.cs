using System;
using System.Threading;
using System.Threading.Tasks;
using Coravel.Invocable;
using Coravel.Scheduling.Invocable;
using Coravel.Scheduling.Schedule.Cron;
using Coravel.Scheduling.Schedule.Interfaces;
using Coravel.Scheduling.Schedule.Zoned;
using Coravel.Scheduling.Tasks;
using SimpleInjector;

namespace Coravel.Scheduling.Schedule.Event
{
    public interface IScheduledEvent : IScheduleInterval, IScheduledEventConfiguration
    {
        string OverlappingUniqueIdentifier { get; }
        bool IsScheduledCronBasedTask { get; }
        bool ShouldRunOnceAtStart { get; }
        Type InvocableType { get; }
        bool ShouldPreventOverlapping { get; }
        bool IsDue(DateTime utcDate);
        Task InvokeScheduledEvent(CancellationToken cancellationToken);
    }

    internal sealed class ScheduledEvent : IScheduledEvent
    {
        private const int OneMinuteAsSeconds = 60;
        private readonly ActionOrAsyncFunc _scheduledAction;
        private readonly IScopeFactory _scopeFactory;
        private CronExpression _expression;
        private bool _isScheduledPerSecond;
        private bool _runOnce;
        private int? _secondsInterval;
        private bool _wasPreviouslyRun;
        private Func<Task<bool>> _whenPredicate;
        private ZonedTime _zonedTime = ZonedTime.AsUTC();

        public ScheduledEvent(Action scheduledAction, IScopeFactory scopeFactory) : this(scopeFactory)
        {
            _scheduledAction = new ActionOrAsyncFunc(scheduledAction);
        }

        public ScheduledEvent(Func<Task> scheduledAsyncTask, IScopeFactory scopeFactory) : this(scopeFactory)
        {
            _scheduledAction = new ActionOrAsyncFunc(scheduledAsyncTask);
        }

        private ScheduledEvent(IScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            OverlappingUniqueIdentifier = Guid.NewGuid().ToString();
        }

        public bool ShouldPreventOverlapping { get; private set; }
        public string OverlappingUniqueIdentifier { get; private set; }
        public bool IsScheduledCronBasedTask => !_isScheduledPerSecond;
        public Type InvocableType { get; private init; }
        public bool ShouldRunOnceAtStart { get; private set; }

        public static ScheduledEvent WithInvocable<T>(IScopeFactory scopeFactory) where T : IInvocable
        {
            return WithInvocableType(typeof(T), scopeFactory);
        }

        public static ScheduledEvent WithInvocableType(Type invocableType, IScopeFactory scopeFactory)
        {
            ScheduledEvent scheduledEvent = new ScheduledEvent(scopeFactory) {InvocableType = invocableType};
            return scheduledEvent;
        }

        public bool IsDue(DateTime utcNow)
        {
            DateTime zonedNow = _zonedTime.Convert(utcNow);

            if (!_isScheduledPerSecond)
            {
                return _expression?.IsDue(zonedNow) ?? false;
            }

            bool isSecondDue = IsSecondsDue(zonedNow);
            bool isWeekDayDue = _expression.IsWeekDayDue(zonedNow);
            return isSecondDue && isWeekDayDue;
        }

        public async Task InvokeScheduledEvent(CancellationToken cancellationToken)
        {
            if (await WhenPredicateFails())
            {
                return;
            }

            if (InvocableType is null)
            {
                await _scheduledAction.Invoke();
            }
            else
            {
                await using Scope scope = _scopeFactory.BeginAsyncScope();

                if (scope.GetInstance(InvocableType) is not IInvocable invocable)
                {
                    throw new InvalidCastException();
                }

                if (invocable is ICancellableInvocable cancellableInvokable)
                {
                    cancellableInvokable.CancellationToken = cancellationToken;
                }

                await invocable.Invoke();
            }

            MarkedAsExecutedOnce();
            UnScheduleIfWarranted();
        }

        public IScheduledEventConfiguration Daily()
        {
            _expression = new CronExpression("00 00 * * *");
            return this;
        }

        public IScheduledEventConfiguration DailyAtHour(int hour)
        {
            _expression = new CronExpression($"00 {hour} * * *");
            return this;
        }

        public IScheduledEventConfiguration DailyAt(int hour, int minute)
        {
            _expression = new CronExpression($"{minute} {hour} * * *");
            return this;
        }

        public IScheduledEventConfiguration Hourly()
        {
            _expression = new CronExpression("00 * * * *");
            return this;
        }

        public IScheduledEventConfiguration HourlyAt(int minute)
        {
            _expression = new CronExpression($"{minute} * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryMinute()
        {
            _expression = new CronExpression("* * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryFiveMinutes()
        {
            _expression = new CronExpression("*/5 * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryTenMinutes()
        {
            // todo fix "*/10" in cron part
            _expression = new CronExpression("*/10 * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryFifteenMinutes()
        {
            _expression = new CronExpression("*/15 * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryThirtyMinutes()
        {
            _expression = new CronExpression("*/30 * * * *");
            return this;
        }

        public IScheduledEventConfiguration Weekly()
        {
            _expression = new CronExpression("00 00 * * 1");
            return this;
        }

        public IScheduledEventConfiguration Monthly()
        {
            _expression = new CronExpression("00 00 1 * *");
            return this;
        }

        public IScheduledEventConfiguration Cron(string cronExpression)
        {
            _expression = new CronExpression(cronExpression);
            return this;
        }

        public IScheduledEventConfiguration Monday()
        {
            _expression.AppendWeekDay(DayOfWeek.Monday);
            return this;
        }

        public IScheduledEventConfiguration Tuesday()
        {
            _expression.AppendWeekDay(DayOfWeek.Tuesday);
            return this;
        }

        public IScheduledEventConfiguration Wednesday()
        {
            _expression.AppendWeekDay(DayOfWeek.Wednesday);
            return this;
        }

        public IScheduledEventConfiguration Thursday()
        {
            _expression.AppendWeekDay(DayOfWeek.Thursday);
            return this;
        }

        public IScheduledEventConfiguration Friday()
        {
            _expression.AppendWeekDay(DayOfWeek.Friday);
            return this;
        }

        public IScheduledEventConfiguration Saturday()
        {
            _expression.AppendWeekDay(DayOfWeek.Saturday);
            return this;
        }

        public IScheduledEventConfiguration Sunday()
        {
            _expression.AppendWeekDay(DayOfWeek.Sunday);
            return this;
        }

        public IScheduledEventConfiguration Weekday()
        {
            Monday().Tuesday().Wednesday().Thursday().Friday();
            return this;
        }

        public IScheduledEventConfiguration Weekend()
        {
            Saturday().Sunday();
            return this;
        }

        public IScheduledEventConfiguration PreventOverlapping(string uniqueIdentifier)
        {
            ShouldPreventOverlapping = true;
            return AssignUniqueIdentifier(uniqueIdentifier);
        }

        public IScheduledEventConfiguration When(Func<Task<bool>> predicate)
        {
            _whenPredicate = predicate;
            return this;
        }

        public IScheduledEventConfiguration AssignUniqueIdentifier(string uniqueIdentifier)
        {
            OverlappingUniqueIdentifier = uniqueIdentifier;
            return this;
        }

        public IScheduledEventConfiguration EverySecond()
        {
            _secondsInterval = 1;
            _isScheduledPerSecond = true;
            _expression = new CronExpression("* * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryFiveSeconds()
        {
            _secondsInterval = 5;
            _isScheduledPerSecond = true;
            _expression = new CronExpression("* * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryTenSeconds()
        {
            _secondsInterval = 10;
            _isScheduledPerSecond = true;
            _expression = new CronExpression("* * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryFifteenSeconds()
        {
            _secondsInterval = 15;
            _isScheduledPerSecond = true;
            _expression = new CronExpression("* * * * *");
            return this;
        }

        public IScheduledEventConfiguration EveryThirtySeconds()
        {
            _secondsInterval = 30;
            _isScheduledPerSecond = true;
            _expression = new CronExpression("* * * * *");
            return this;
        }

        public IScheduledEventConfiguration EverySeconds(int seconds)
        {
            if (seconds < 1 || seconds > 59)
            {
                throw new ArgumentException("When calling 'EverySeconds(int seconds)', 'seconds' must be between 0 and 60");
            }

            _secondsInterval = seconds;
            _isScheduledPerSecond = true;
            _expression = new CronExpression("* * * * *");
            return this;
        }
        
        public IScheduledEventConfiguration Zoned(TimeZoneInfo timeZoneInfo)
        {
            _zonedTime = new ZonedTime(timeZoneInfo);
            return this;
        }

        public IScheduledEventConfiguration RunOnceAtStart()
        {
            ShouldRunOnceAtStart = true;
            return this;
        }

        public IScheduledEventConfiguration Once()
        {
            _runOnce = true;
            return this;
        }

        private async Task<bool> WhenPredicateFails()
        {
            return _whenPredicate != null && !await _whenPredicate.Invoke();
        }

        private bool IsSecondsDue(DateTime utcNow)
        {
            if (utcNow.Second == 0)
            {
                return OneMinuteAsSeconds % _secondsInterval == 0;
            }

            return utcNow.Second % _secondsInterval == 0;
        }

        private bool PreviouslyRanAndMarkedToRunOnlyOnce()
        {
            return _runOnce && _wasPreviouslyRun;
        }

        private void MarkedAsExecutedOnce()
        {
            _wasPreviouslyRun = true;
        }

        private void UnScheduleIfWarranted()
        {
            if (!PreviouslyRanAndMarkedToRunOnlyOnce())
            {
                return;
            }

            using var scope = _scopeFactory.BeginAsyncScope();
            var scheduler = scope.GetInstance<IScheduler>() as Scheduler;
            scheduler.TryUnschedule(OverlappingUniqueIdentifier);
        }
    }
}