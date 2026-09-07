using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventCustomPropertyProjectionRepository : IEventCustomPropertyProjectionRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventCustomPropertyProjectionRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<EventCustomPropertyProjection>> GetForEventAsync(
        Guid eventId,
        ExposureLevel? exposureCeiling,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.EventCustomPropertyProjections
            .AsNoTracking()
            .Where(p => p.EventId == eventId);

        if (exposureCeiling.HasValue)
        {
            var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(exposureCeiling.Value);
            query = query.Where(p => visibleExposureLevels.Contains(p.ExposureLevel));
        }

        return await query
            .OrderBy(p => p.Namespace)
            .ThenBy(p => p.Key)
            .ThenBy(p => p.Ordinal)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountActiveDefinitionsForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventCustomPropertyDefinitions
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive)
            .Select(d => new { d.Namespace, d.Key })
            .Distinct()
            .CountAsync(cancellationToken);
    }
}
