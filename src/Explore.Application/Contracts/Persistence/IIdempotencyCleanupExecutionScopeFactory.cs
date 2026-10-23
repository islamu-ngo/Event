namespace Explore.Application.Contracts.Persistence;

public static class IdempotencyCleanupLimits
{
    public const int MaximumRows = 1000;
}

public interface IIdempotencyCleanupTenantDiscoveryRepository
{
    Task<IReadOnlyList<Guid>> GetExpiredTenantIdsAsync(
        DateTime expiresBeforeUtc, int maximumRows, CancellationToken cancellationToken = default);
}

/// <summary>Fresh selected-tenant maintenance custody, exposing no replay or claim methods.</summary>
public interface IIdempotencyCleanupExecutionScope : IAsyncDisposable
{
    Task<int> CountExpiredAsync(DateTime expiresBeforeUtc, int maximumRows, CancellationToken cancellationToken = default);
    Task<int> ScrubExpiredAsync(DateTime expiresBeforeUtc, int maximumRows, CancellationToken cancellationToken = default);
}

public interface IIdempotencyCleanupExecutionScopeFactory
{
    Task<IIdempotencyCleanupExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
