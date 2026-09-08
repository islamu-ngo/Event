using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventDayRepository : GenericRepository<EventDay, Guid>, IEventDayRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventDayRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<EventDay?> GetByIdForEventAsync(
        Guid eventDayId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        _dbContext.EventDays
            .AsNoTracking()
            .FirstOrDefaultAsync(
                day => day.Id == eventDayId && day.EventId == eventId && day.TenantId == tenantId,
                cancellationToken);

    public async Task<EventDay?> GetByIdForEventForUpdateAsync(
        Guid eventDayId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence.AcquireAsync<EventDay>(
            _dbContext,
            tenantId,
            day => day.Id,
            eventDayId,
            cancellationToken);

        return await _dbContext.EventDays.FirstOrDefaultAsync(
            day => day.Id == eventDayId && day.EventId == eventId && day.TenantId == tenantId,
            cancellationToken);
    }

    public async Task<bool> BelongsToEventAsync(Guid eventDayId, Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventDays
            .AsNoTracking()
            .AnyAsync(d => d.Id == eventDayId && d.EventId == eventId, cancellationToken);
    }

    public async Task<List<EventDay>> GetByEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventDays
            .AsNoTracking()
            .Where(d => d.EventId == eventId)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.LocalDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<EventDay?> FindByEventAndLocalDateAsync(Guid eventId, DateOnly localDate, CancellationToken cancellationToken)
    {
        return await _dbContext.EventDays
            .AsNoTracking()
            .Where(d => d.EventId == eventId && d.LocalDate == localDate)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
