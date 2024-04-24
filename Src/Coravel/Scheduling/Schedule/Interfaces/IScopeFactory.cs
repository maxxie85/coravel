using SimpleInjector;

namespace Coravel.Scheduling.Schedule.Interfaces;

public interface IScopeFactory
{
    public Scope BeginAsyncScope();
}