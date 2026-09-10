using Explore.Application.Contracts.Persistence;

namespace Event.Application.UnitTests.Features.EventPublicActions.Commands;

internal sealed class EventPublicActionTestUnitOfWork : IUnitOfWork
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        ExecuteSerializableAsync(async innerCt =>
        {
            await operation(innerCt);
            return true;
        }, ct);

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        ExecuteSerializableAsync(operation, ct);

    public async Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await operation(ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        ExecuteInTransactionAsync(operation, ct);
}
