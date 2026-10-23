namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Owns one Serializable attempt without replaying a prepared authorization decision.
/// Rollback-known conflicts must restart preparation outside all database leases.
/// </summary>
public interface ISerializableAttemptOwner
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
