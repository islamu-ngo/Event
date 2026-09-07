namespace Explore.Application.Contracts.Persistence;

public interface IUnitOfWork
{
    /// <summary>
    /// Executes the operation inside a single database transaction.
    /// Compatible with NpgsqlRetryingExecutionStrategy.
    /// The delegate MUST be idempotent — it may be retried on transient Postgres failures.
    /// Never place emails, HTTP calls, or broker publishes inside the delegate.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);

    /// <summary>
    /// Returns-value variant. Prefer this over the void overload to avoid closure-capture boilerplate.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    /// <summary>
    /// Executes capacity-sensitive work under the provider execution strategy and a serializable transaction.
    /// Stable identities and timestamps must be created before entering the retryable delegate.
    /// </summary>
    Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    /// <summary>
    /// Executes bootstrap convergence under serializable isolation, retrying the complete transaction only for
    /// provider-recognized serialization, deadlock, unique-key, or busy conflicts.
    /// </summary>
    Task<T> ExecuteBootstrapConvergenceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default) =>
        ExecuteSerializableAsync(operation, ct);
}
