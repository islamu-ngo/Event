namespace Explore.Persistence.Database;

/// <summary>Acquires only the independently configured payment-reconciliation Global pool.</summary>
public interface IPaymentReconciliationAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
