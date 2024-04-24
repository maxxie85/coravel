using System;
using System.Threading.Tasks;
using Coravel.Invocable;
using Coravel.Scheduling.Schedule.Event;
using Coravel.Scheduling.Schedule.Interfaces;
using SimpleInjector;

namespace Coravel.Scheduling.Extensions;

public static class ContainerExtensions
{
    /// <summary>
    ///     Schedule a task.
    /// </summary>
    /// <param name="container"></param>
    /// <param name="actionToSchedule">Task to schedule.</param>
    /// <returns></returns>
    public static IScheduleInterval Schedule(this Container container, Action actionToSchedule)
    {
        ScheduledEvent scheduled = new ScheduledEvent(actionToSchedule, container.GetInstance<IScopeFactory>());
        container.Collection.AppendInstance((IScheduledEvent) scheduled);
        return scheduled;
    }

    /// <summary>
    ///     Schedule an asynchronous task.
    /// </summary>
    /// <param name="container"></param>
    /// <param name="asyncTaskToSchedule">Async task to schedule.</param>
    /// <returns></returns>
    public static IScheduleInterval ScheduleAsync(this Container container, Func<Task> asyncTaskToSchedule)
    {
        ScheduledEvent scheduled = new ScheduledEvent(asyncTaskToSchedule, container.GetInstance<IScopeFactory>());
        container.Collection.AppendInstance((IScheduledEvent) scheduled);
        return scheduled;
    }

    /// <summary>
    ///     Schedule an Invocable job.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static IScheduleInterval Schedule<T>(this Container container) where T : IInvocable
    {
        ScheduledEvent scheduled = ScheduledEvent.WithInvocable<T>(container.GetInstance<IScopeFactory>());
        container.Collection.AppendInstance((IScheduledEvent) scheduled);
        return scheduled;
    }

    /// <summary>
    ///     Schedule an Invocable job.
    ///     InvocableType param must be assignable from and implement the IInvocable interface.
    /// </summary>
    /// <param name="container"></param>
    /// <param name="invocableType"></param>
    /// <returns></returns>
    public static IScheduleInterval ScheduleInvocableType(this Container container, Type invocableType)
    {
        if (!typeof(IInvocable).IsAssignableFrom(invocableType))
        {
            throw new Exception("ScheduleInvocableType must be passed in a type that implements IInvocable.");
        }

        ScheduledEvent scheduled = ScheduledEvent.WithInvocableType(invocableType, container.GetInstance<IScopeFactory>());
        container.Collection.AppendInstance((IScheduledEvent) scheduled);
        return scheduled;
    }
}