namespace Explore.Persistence.Database;

/// <summary>Creates identity-free leases from the separately configured Global credential pool.</summary>
public interface IGlobalExploreDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
