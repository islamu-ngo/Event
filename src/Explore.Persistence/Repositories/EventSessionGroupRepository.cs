using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventSessionGroupRepository : GenericRepository<EventSessionGroup, Guid>, IEventSessionGroupRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSessionGroupRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventSessionGroup?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroups
            .AsNoTrackingWithIdentityResolution()
            .Include(group => group.Event)
            .Include(group => group.Location)
            .Include(group => group.Room)
            .FirstOrDefaultAsync(group => group.Id == id, cancellationToken);
    }

    public async Task<EventSessionGroup?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroups
            .Include(group => group.Event)
            .Include(group => group.Location)
            .Include(group => group.Room)
            .FirstOrDefaultAsync(group => group.Id == id, cancellationToken);
    }

    public async Task<EventSessionGroup?> GetPublicWithDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroups
            .AsNoTrackingWithIdentityResolution()
            .Include(group => group.Event)
            .Include(group => group.Location)
            .Include(group => group.Room)
            .WherePubliclyEligible(_dbContext)
            .FirstOrDefaultAsync(group => group.Id == id, cancellationToken);
    }

    public async Task<List<EventSessionGroup>> GetByEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroups
            .AsNoTrackingWithIdentityResolution()
            .Include(group => group.Location)
            .Include(group => group.Room)
            .Where(group => group.EventId == eventId && group.IsPublished)
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<EventSessionGroup>> GetPublicByEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroups
            .AsNoTrackingWithIdentityResolution()
            .Include(group => group.Event)
            .Include(group => group.Location)
            .Include(group => group.Room)
            .WherePubliclyEligible(_dbContext)
            .Where(group => group.EventId == eventId)
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<EventSessionGroup>> GetActiveByEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroups
            .AsNoTrackingWithIdentityResolution()
            .Include(group => group.Location)
            .Include(group => group.Room)
            .Where(group => group.EventId == eventId)
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Name)
            .ToListAsync(cancellationToken);
    }
}
