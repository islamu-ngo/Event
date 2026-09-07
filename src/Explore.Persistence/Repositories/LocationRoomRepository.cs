using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class LocationRoomRepository : GenericRepository<LocationRoom, Guid>, ILocationRoomRepository
{
    private readonly ExploreDbContext _dbContext;

    public LocationRoomRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<LocationRoom>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        RequireTenant();
        Guid[] normalizedIds = ids.Distinct().ToArray();
        if (normalizedIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("LocationRoom ids must be non-empty.", nameof(ids));
        }

        if (normalizedIds.Length > ILocationRoomRepository.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ids),
                $"LocationRoom batches cannot exceed {ILocationRoomRepository.MaximumBatchSize} unique ids.");
        }

        if (normalizedIds.Length == 0)
        {
            return [];
        }

        return await _dbContext.LocationRooms
            .AsNoTracking()
            .Where(room => normalizedIds.Contains(room.Id))
            .OrderBy(room => room.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<LocationRoom>> GetByLocationAsync(Guid locationId, CancellationToken cancellationToken)
    {
        return await _dbContext.LocationRooms
            .AsNoTracking()
            .Where(r => r.LocationId == locationId)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasActiveScheduleReferencesAsync(
        Guid roomId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessions.AnyAsync(
                item => item.RoomId == roomId,
                cancellationToken)
            || await _dbContext.EventSessionGroups.AnyAsync(
                item => item.RoomId == roomId,
                cancellationToken)
            || await _dbContext.EventAgendaItems.AnyAsync(
                item => item.RoomId == roomId,
                cancellationToken);
    }

    private void RequireTenant()
    {
        if (!_dbContext.IsTenantFilterBypassed && !_dbContext.TenantFilterTenantId.HasValue)
        {
            throw new InvalidOperationException("A tenant context is required for LocationRoom persistence.");
        }
    }
}
