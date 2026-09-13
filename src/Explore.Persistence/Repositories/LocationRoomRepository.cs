using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

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

    public async Task<bool> HasScheduleReferencesAsync(
        Guid roomId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessions.IgnoreQueryFilters(["SoftDelete"]).AnyAsync(
                item => item.RoomId == roomId,
                cancellationToken)
            || await _dbContext.EventSessionGroups.IgnoreQueryFilters(["SoftDelete"]).AnyAsync(
                item => item.RoomId == roomId,
                cancellationToken)
            || await _dbContext.EventAgendaItems.IgnoreQueryFilters(["SoftDelete"]).AnyAsync(
                item => item.RoomId == roomId,
                cancellationToken);
    }

    public async Task MoveToLocationAsync(
        LocationRoom room,
        Location location,
        string name,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Moving a location room requires an active transaction.");
        }

        var originalLocationId = room.LocationId;
        _dbContext.Entry(room).State = EntityState.Detached;
        int affectedRows;
        try
        {
            affectedRows = await _dbContext.LocationRooms
                .Where(candidate =>
                    candidate.TenantId == room.TenantId
                    && candidate.Id == room.Id
                    && candidate.LocationId == originalLocationId
                    && !candidate.IsDeleted
                    && candidate.ConcurrencyStamp == expectedConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.LocationId, location.Id)
                    .SetProperty(candidate => candidate.Name, name),
                    cancellationToken);
        }
        catch (DbException exception) when (exception is
            PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation }
            or SqliteException { SqliteExtendedErrorCode: 787 }
            or SqlException { Number: 547 }
            or MySqlException { Number: 1451 or 1452 })
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The room's location or schedule references changed. Reload and retry.",
                nameof(LocationRoom),
                room.Id.ToString(),
                exception);
        }
        catch (DbException exception) when (exception is
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            or SqliteException { SqliteExtendedErrorCode: 2067 }
            or SqlException { Number: 2601 or 2627 }
            or MySqlException { Number: 1062 })
        {
            throw new BadRequestException("A room with that name already exists at the destination location.");
        }
        if (affectedRows != 1)
        {
            throw new DbUpdateConcurrencyException("The room changed before it could be moved.");
        }

        room.LocationId = location.Id;
        room.Location = location;
        room.Name = name;
        _dbContext.LocationRooms.Attach(room);
    }

    private void RequireTenant()
    {
        if (!_dbContext.IsTenantFilterBypassed && !_dbContext.TenantFilterTenantId.HasValue)
        {
            throw new InvalidOperationException("A tenant context is required for LocationRoom persistence.");
        }
    }
}
