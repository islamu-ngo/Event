namespace Explore.Persistence.Database;

/// <summary>Acquires an identity-free Global lease for bounded PDS queue admission.</summary>
public interface IPdsSyncQueueAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
