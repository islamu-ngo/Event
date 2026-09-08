namespace Explore.Persistence.Repositories;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Repository implementation for EventIslamicAspect entity.
/// </summary>
public class EventIslamicAspectRepository : GenericRepository<EventIslamicAspect, Guid>, IEventIslamicAspectRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventIslamicAspectRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<EventIslamicAspect?> GetByEventIdWithDetails(Guid eventId)
    {
        return await _dbContext.EventIslamicAspects
            .Include(a => a.Madhab)
            .Include(a => a.PrimaryLanguage)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == eventId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventIslamicAspect>> GetByGenderMode(GenderSegregationMode genderMode)
    {
        return await _dbContext.EventIslamicAspects
            .Include(a => a.Madhab)
            .Include(a => a.PrimaryLanguage)
            .Where(a => a.GenderMode == genderMode)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventIslamicAspect>> GetByMadhab(int madhabId)
    {
        return await _dbContext.EventIslamicAspects
            .Include(a => a.Madhab)
            .Include(a => a.PrimaryLanguage)
            .Where(a => a.MadhabId == madhabId)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<EventIslamicAspect> Upsert(EventIslamicAspect aspect)
    {
        var existing = await _dbContext.EventIslamicAspects
            .FirstOrDefaultAsync(a => a.Id == aspect.Id);

        if (existing == null)
        {
            await _dbContext.EventIslamicAspects.AddAsync(aspect);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(aspect);
        }

        await _dbContext.SaveChangesAsync();
        return aspect;
    }
}
