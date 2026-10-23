namespace Explore.Persistence.Database;

/// <summary>Unbound identity-only admission; never used for business execution.</summary>
public interface IGenericOutboxAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
