using System.Threading;

namespace Coravel.Invocable
{
    public interface ICancellableInvocable : IInvocable
    {
        CancellationToken CancellationToken { get; set; }
    }
}