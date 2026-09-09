using Explore.Application.Contracts.Persistence;

namespace Event.Application.UnitTests.Features.EventTicketing;

internal sealed class TicketingTestUnitOfWork(Exception? commitFailure = null) : IUnitOfWork
{
    public int TransactionBoundaries { get; private set; }

    public bool HasCommitted { get; private set; }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        ExecuteInTransactionAsync<object?>(async token =>
        {
            await operation(token);
            return null;
        }, ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        TransactionBoundaries++;
        T result = await operation(ct);
        if (commitFailure is not null)
        {
            throw commitFailure;
        }

        HasCommitted = true;
        return result;
    }

    public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        ExecuteInTransactionAsync(operation, ct);

    public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        ExecuteInTransactionAsync(operation, ct);
}
