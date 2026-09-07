using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventSessionCustomPropertyProjectionRepository : IEventSessionCustomPropertyProjectionRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSessionCustomPropertyProjectionRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<EventSessionCustomPropertyProjection>> GetForSessionAsync(
        Guid eventSessionId,
        ExposureLevel? exposureCeiling,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.EventSessionCustomPropertyProjections
            .AsNoTracking()
            .Where(p => p.EventSessionId == eventSessionId);

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
}
