namespace Explore.Persistence.Database;

/// <summary>Acquires identity-free Global leases for fanout ledger admission and aggregate health only.</summary>
public interface INotificationFanoutAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
