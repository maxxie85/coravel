using System.Threading;
using Coravel.Invocable;

namespace Coravel.Scheduling.Invocable
{
    public interface ICancellableInvocable : IInvocable
    {
        CancellationToken CancellationToken { get; set; }
    }
}