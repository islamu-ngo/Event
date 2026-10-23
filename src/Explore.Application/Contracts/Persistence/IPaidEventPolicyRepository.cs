using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IPaidEventPolicyRepository
{
    const int MaximumActiveTenantPolicyPageSize = 256;

    Task<PaidEventPolicyVersion?> GetActiveInstanceAsync(CancellationToken cancellationToken);

    Task<PaidEventPolicyVersion?> GetActiveTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads fixed, untracked active tenant validation aggregates in the owning context's
    /// transaction. PostgreSQL uses execute-only ceiling authority, not a second lease.
    /// </summary>
    Task<PaidEventPolicyVersion[]> ListActiveTenantsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<PaidEventPolicyVersion[]> ListTenantHistoryAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddAsync(PaidEventPolicyVersion policy, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
