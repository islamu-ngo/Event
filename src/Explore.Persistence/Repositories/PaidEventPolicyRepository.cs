using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class PaidEventPolicyRepository(ExploreDbContext dbContext) : IPaidEventPolicyRepository
{
    private const string ActiveTenantPolicyValidationBypassReason =
        "Instance paid-policy revision validates bounded pages of every active tenant policy before narrowing the instance ceiling.";

    public Task<PaidEventPolicyVersion?> GetActiveInstanceAsync(CancellationToken cancellationToken) =>
        dbContext.PaidEventPolicyVersions
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .SingleOrDefaultAsync(policy => policy.TenantId == null && policy.IsActive, cancellationToken);

    public Task<PaidEventPolicyVersion?> GetActiveTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.PaidEventPolicyVersions
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .SingleOrDefaultAsync(policy => policy.TenantId == tenantId && policy.IsActive, cancellationToken);

    public async Task<PaidEventPolicyVersion[]> ListActiveTenantsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Active tenant paid-policy pages must contain between 1 and {IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize} entities.");
        }

        return await dbContext.PaidEventPolicyVersions
            .IgnoreAllFilters(ActiveTenantPolicyValidationBypassReason)
            .AsNoTracking()
            .Where(policy => policy.TenantId != null && policy.IsActive)
            .OrderBy(policy => policy.TenantId)
            .Skip(offset)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<PaidEventPolicyVersion[]> ListTenantHistoryAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.PaidEventPolicyVersions
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(policy => policy.TenantId == tenantId)
            .OrderBy(policy => policy.VersionNumber)
            .ToArrayAsync(cancellationToken);

    public async Task AddAsync(PaidEventPolicyVersion policy, CancellationToken cancellationToken) =>
        await dbContext.PaidEventPolicyVersions.AddAsync(policy, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
