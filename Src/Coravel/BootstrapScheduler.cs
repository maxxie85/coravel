using System;
using Coravel.Scheduling.Extensions;
using Coravel.Scheduling.HostedService;
using Coravel.Scheduling.Schedule;
using Coravel.Scheduling.Schedule.Interfaces;
using Coravel.Scheduling.Schedule.Mutex;
using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace Coravel
{
    /// <summary>
    ///     IServiceCollection extensions for registering Coravel's Scheduler.
    /// </summary>
    public static class BootstrapScheduler
    {
        public static void RegisterScheduler(this Container container)
        {
            container.RegisterSingleton<IMutex, InMemoryMutex>();
            container.RegisterSingleton<IScheduler, Scheduler>();
            container.Register<ISchedulerService, SchedulerService>();
            
            container.Schedule(() => {});
        }

        private sealed class ScopeFactory : IScopeFactory
        {
            private readonly Container _container;

            public ScopeFactory(Container container)
            {
                _container = container;
            }

            public Scope BeginAsyncScope()
            {
                return AsyncScopedLifestyle.BeginScope(_container);
            }
        }
    }
}