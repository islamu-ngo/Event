namespace Explore.Persistence.Database;

/// <summary>Creates ordinary Runtime leases sealed to one exact tenant, including deleted owners.</summary>
public interface ITenantExploreDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
