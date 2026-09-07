namespace Explore.Persistence.Repositories;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Repository implementation for EventTechAspect entity.
/// </summary>
public class EventTechAspectRepository : GenericRepository<EventTechAspect, Guid>, IEventTechAspectRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventTechAspectRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<EventTechAspect?> GetByEventId(Guid eventId)
    {
        return await _dbContext.EventTechAspects
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == eventId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventTechAspect>> GetBySkillLevel(SkillLevel skillLevel)
    {
        return await _dbContext.EventTechAspects
            .Where(a => a.SkillLevel == skillLevel)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventTechAspect>> GetCodingCompetitions()
    {
        return await _dbContext.EventTechAspects
            .Where(a => a.IsCodingCompetition)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventTechAspect>> GetHackathons()
    {
        return await _dbContext.EventTechAspects
            .Where(a => !string.IsNullOrEmpty(a.HackathonTrack))
            .AsNoTracking()
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<EventTechAspect> Upsert(EventTechAspect aspect)
    {
        var existing = await _dbContext.EventTechAspects
            .FirstOrDefaultAsync(a => a.Id == aspect.Id);

        if (existing == null)
        {
            await _dbContext.EventTechAspects.AddAsync(aspect);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(aspect);
        }

        await _dbContext.SaveChangesAsync();
        return aspect;
    }
}
