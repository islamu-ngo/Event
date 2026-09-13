using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Api.IntegrationTests.Features;

internal sealed class NotificationTransactionGate : DbTransactionInterceptor
{
    private int _armed;
    public TaskCompletionSource<IsolationLevel> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Arm() => Interlocked.Exchange(ref _armed, 1);

    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _armed, 0) == 1)
        {
            Entered.TrySetResult(eventData.IsolationLevel);
            try
            {
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            finally
            {
                Exited.TrySetResult();
            }
        }
        return result;
    }
}
