namespace Explore.Persistence.Database;

/// <summary>
/// Acquires Global settlement credentials with a persisted, immutable exact tenant
/// before exposing the context. The caller owns disposal and the settlement transaction.
/// </summary>
public interface IPdsSettlementDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(
        Guid tenantId, CancellationToken cancellationToken = default);
}
