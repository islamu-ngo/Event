using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventTypeRepository : GenericRepository<EventType, int>, IEventTypeRepository
{
    private readonly ExploreDbContext _dbContext;
    public EventTypeRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }
    public async Task<List<EventType>> GetEventTypesWithDetails()
    {
        var eventTypes = await _dbContext.EventTypes
            .AsNoTracking()
            .ToListAsync();
        return eventTypes;
    }

    public async Task<EventType?> GetEventTypeWithDetails(int id)
    {
        var eventType = await _dbContext.EventTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);
        return eventType;
    }
}
