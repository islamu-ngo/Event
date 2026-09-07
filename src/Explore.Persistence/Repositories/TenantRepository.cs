using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantRepository : GenericRepository<Tenant, Guid>, ITenantRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Tenant?> GetTenantBySlug(string slug)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug);
    }

    public async Task<IReadOnlyList<Tenant>> GetBySlugsAsNoTrackingAsync(
        IReadOnlyCollection<string> slugs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        if (slugs.Count == 0)
        {
            return [];
        }

        string[] normalizedSlugs = slugs
            .Select(slug => slug?.Trim())
            .Where(slug => !string.IsNullOrEmpty(slug))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();
        if (normalizedSlugs.Length != slugs.Count)
        {
            throw new ArgumentException(
                "Tenant slug batches must contain unique non-empty values.",
                nameof(slugs));
        }

        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => normalizedSlugs.Contains(tenant.Slug))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetActiveTenantCountAsync()
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .CountAsync(t => t.TenantStatus.IsActiveState);
    }

    public async Task<IReadOnlyList<Tenant>> GetActiveAsNoTrackingAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.TenantStatus.IsActiveState)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>>
        GetAllActiveForConfigurationManifestExportAsync(
            int maximumCount,
            CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        return await _dbContext.Tenants
            .IgnoreTenantFilter(
                TenantFilterBypassReasons.InstanceConfigurationManifestExport)
            .AsNoTracking()
            .Where(tenant => tenant.TenantStatus.IsActiveState)
            .OrderBy(tenant => tenant.Slug)
            .ThenBy(tenant => tenant.Id)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
    }

    public Task<Tenant?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);

    public async Task<bool> TryTransitionStatusAsync(
        Guid id,
        int expectedStatusId,
        int newStatusId,
        DateTime updatedAt,
        Guid updatedBy,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await _dbContext.Tenants
            .Where(tenant => tenant.Id == id && tenant.TenantStatusId == expectedStatusId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(tenant => tenant.TenantStatusId, newStatusId)
                .SetProperty(tenant => tenant.UpdatedAt, updatedAt)
                .SetProperty(tenant => tenant.UpdatedBy, updatedBy), cancellationToken);

        return affectedRows == 1;
    }
}
