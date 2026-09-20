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
        PromotionRecommendation? recommendationFilter,
        int totalEventCount,
        CancellationToken cancellationToken)
    {
        var eventDefs = _dbContext.EventCustomPropertyDefinitions
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive)
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                d.Namespace,
                d.Key,
                d.DisplayName,
                EntityScope = "Event",
                d.PropertyType,
                d.ExposureLevel,
                d.IsSearchable,
                d.IsFilterable,
                d.IsExportable,
                d.IsModerationRelevant,
                d.IsAnalyticsRelevant,
                d.IsSystemOwned,
                ActiveInstanceCount = d.Values.Count,
                LastUsedAt = d.Values.Max(v => v.UpdatedAt)
            });

        var sessionDefs = _dbContext.EventSessionCustomPropertyDefinitions
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive)
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                d.Namespace,
                d.Key,
                d.DisplayName,
                EntityScope = "EventSession",
                d.PropertyType,
                d.ExposureLevel,
                d.IsSearchable,
                d.IsFilterable,
                d.IsExportable,
                d.IsModerationRelevant,
                d.IsAnalyticsRelevant,
                d.IsSystemOwned,
                ActiveInstanceCount = d.Values.Count,
                LastUsedAt = d.Values.Max(v => v.UpdatedAt)
            });

        var combined = eventDefs.Concat(sessionDefs);

        if (!string.IsNullOrEmpty(entityScopeFilter))
        {
            combined = combined.Where(r => r.EntityScope == entityScopeFilter);
        }

        if (recommendationFilter.HasValue)
        {
            combined = combined.Where(r =>
                (r.IsModerationRelevant && (r.IsSearchable || r.IsFilterable)
                    && totalEventCount > 0 && (long)r.ActiveInstanceCount * 100 / totalEventCount >= 30
                    ? PromotionRecommendation.ConsiderLayer1Promotion
                    : r.IsModerationRelevant || r.IsAnalyticsRelevant
                        ? PromotionRecommendation.ConsiderLayer2Promotion
                        : r.IsSearchable || r.IsFilterable
                            ? PromotionRecommendation.ConsiderProjectionFirst
                            : PromotionRecommendation.None) == recommendationFilter.Value);
        }

        var totalCount = await combined.CountAsync(cancellationToken);

        var items = await combined
            .OrderBy(r => r.EntityScope)
            .ThenBy(r => r.Namespace)
            .ThenBy(r => r.Key)
            .ThenBy(r => r.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new GovernanceDefinitionRow(
                r.TenantId, r.Namespace, r.Key, r.DisplayName, r.EntityScope,
                r.PropertyType, r.ExposureLevel, r.IsSearchable, r.IsFilterable,
                r.IsExportable, r.IsModerationRelevant, r.IsAnalyticsRelevant,
                r.IsSystemOwned, r.ActiveInstanceCount, r.LastUsedAt))
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
