using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventSessionSpeakerRepository : GenericRepository<EventSessionSpeaker, Guid>, IEventSessionSpeakerRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSessionSpeakerRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<EventSessionSpeaker>> GetBySession(
        Guid eventSessionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.EventSessionSpeakers
            .AsNoTracking()
            .Include(s => s.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(s => s.EventSession)
            .Where(s => s.EventSessionId == eventSessionId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<EventSessionSpeaker>> GetByActor(
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.EventSessionSpeakers
            .AsNoTracking()
            .Include(s => s.EventSession)
            .Where(s => s.ActorId == actorId)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<EventSessionSpeaker> Items, int TotalCount)> GetSpeakersWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EventSessionSpeakers
            .AsNoTracking()
            .Include(s => s.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(s => s.EventSession)
                .ThenInclude(es => es.Event)
            .OrderByDescending(s => s.Id);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<EventSessionSpeaker?> GetBySessionAndActor(
        Guid eventSessionId,
        Guid actorId,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EventSessionSpeakers
            .AsNoTracking()
            .Where(s => s.EventSessionId == eventSessionId && s.ActorId == actorId);

        if (excludeId.HasValue)
        {
            query = query.Where(s => s.Id != excludeId.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }
}
