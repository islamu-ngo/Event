using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class CustomPropertyGovernanceRepository : ICustomPropertyGovernanceRepository
{
    private readonly ExploreDbContext _dbContext;

    public CustomPropertyGovernanceRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(List<GovernanceDefinitionRow> Items, int TotalCount)> GetGovernanceRowsAsync(
        Guid tenantId,
        string? entityScopeFilter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var eventDefs = _dbContext.EventCustomPropertyDefinitions
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive)
            .Select(d => new GovernanceDefinitionRow(
                d.TenantId,
                d.Namespace,
                d.Key,
                d.DisplayName,
                "Event",
                d.PropertyType,
                d.ExposureLevel,
                d.IsSearchable,
                d.IsFilterable,
                d.IsExportable,
                d.IsModerationRelevant,
                d.IsAnalyticsRelevant,
                d.IsSystemOwned,
                d.Values!.Count,
                d.Values!.Max(v => v.UpdatedAt)));

        var sessionDefs = _dbContext.EventSessionCustomPropertyDefinitions
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive)
            .Select(d => new GovernanceDefinitionRow(
                d.TenantId,
                d.Namespace,
                d.Key,
                d.DisplayName,
                "EventSession",
                d.PropertyType,
                d.ExposureLevel,
                d.IsSearchable,
                d.IsFilterable,
                d.IsExportable,
                d.IsModerationRelevant,
                d.IsAnalyticsRelevant,
                d.IsSystemOwned,
                d.Values!.Count,
                d.Values!.Max(v => v.UpdatedAt)));

        var combined = eventDefs.Concat(sessionDefs);

        if (!string.IsNullOrEmpty(entityScopeFilter))
        {
            combined = combined.Where(r => r.EntityScope == entityScopeFilter);
        }

        var totalCount = await combined.CountAsync(cancellationToken);

        var items = await combined
            .OrderBy(r => r.EntityScope)
            .ThenBy(r => r.Namespace)
            .ThenBy(r => r.Key)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<int> GetTotalEventCountForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Events
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .CountAsync(cancellationToken);
    }
}
