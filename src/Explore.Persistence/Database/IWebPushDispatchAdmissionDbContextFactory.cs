namespace Explore.Persistence.Database;

/// <summary>Acquires independent Global leases restricted to bounded WebPush ledger functions.</summary>
public interface IWebPushDispatchAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
