using System.Data.Common;
using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public async Task<LocationRoom?> GetForMutationAsync(
        Guid roomId, Guid? destinationLocationId, CancellationToken cancellationToken)
    {
        if (_dbContext.IsTenantFilterBypassed || _dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Room mutation requires the exact Runtime tenant Serializable transaction.");
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(_dbContext, tenantId, cancellationToken);
        int? statusId = await _dbContext.Tenants.AsNoTracking().Where(tenant => tenant.Id == tenantId)
            .Select(tenant => (int?)tenant.TenantStatusId).SingleOrDefaultAsync(cancellationToken);
        if (statusId is not { } tenantStatus)
            return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(_dbContext, [tenantStatus], cancellationToken);
        if (!await _dbContext.Tenants.AsNoTracking().AnyAsync(
                tenant => tenant.Id == tenantId && tenant.TenantStatus.IsActiveState, cancellationToken))
            return null;
        await RelationalEntityRowFence.AcquireAsync<LocationRoom>(_dbContext, tenantId, room => room.Id, roomId, cancellationToken);
        var current = await _dbContext.LocationRooms.AsNoTracking()
            .SingleOrDefaultAsync(room => room.Id == roomId && room.TenantId == tenantId, cancellationToken);
        if (current is null || current.IsDeleted)
            return null;
        foreach (Guid id in new[] { current.LocationId, destinationLocationId ?? current.LocationId }.Distinct().Order())
        {
            await RelationalEntityRowFence.AcquireAsync<Location>(_dbContext, tenantId, location => location.Id, id, cancellationToken);
            if (!await _dbContext.Locations.AsNoTracking().AnyAsync(location => location.Id == id && location.TenantId == tenantId
                    && location.LocationPrivacyStateId != (int)LocationPrivacyStateEnum.Erased, cancellationToken))
                return null;
        }
        var tracked = _dbContext.ChangeTracker.Entries<LocationRoom>().SingleOrDefault(entry => entry.Entity.Id == roomId);
        if (tracked is not null && (tracked.Entity.TenantId != current.TenantId || tracked.Entity.LocationId != current.LocationId))
            throw new InvalidOperationException("Tracked room ownership must match its persisted source.");
        if (tracked is not null)
            tracked.State = EntityState.Detached;
        return current;
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

        if (_dbContext.IsTenantFilterBypassed || _dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            throw new InvalidOperationException("Room relocation requires the exact non-bypassed tenant lease.");
        await RelationalEntityRowFence.AcquireAsync<LocationRoom>(
            _dbContext, tenantId, candidate => candidate.Id, room.Id, cancellationToken);
        var storedRoom = await _dbContext.LocationRooms.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == room.Id && candidate.TenantId == tenantId, cancellationToken);
        if (storedRoom is null || storedRoom.TenantId != room.TenantId || storedRoom.LocationId != room.LocationId
            || storedRoom.ConcurrencyStamp != expectedConcurrencyStamp || room.ConcurrencyStamp != expectedConcurrencyStamp)
            throw new DbUpdateConcurrencyException("The room changed before it could be moved.");
        if (storedRoom.IsDeleted)
            throw new InvalidOperationException("An erased room cannot be relocated.");
        Location? storedDestination = null;
        foreach (Guid locationId in new[] { storedRoom.LocationId, location.Id }.Distinct().Order())
        {
            await RelationalEntityRowFence.AcquireAsync<Location>(
                _dbContext, tenantId, candidate => candidate.Id, locationId, cancellationToken);
            var persisted = await _dbContext.Locations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == locationId && candidate.TenantId == tenantId, cancellationToken);
            if (persisted is null || persisted.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased)
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The room's location changed. Reload and retry.", nameof(LocationRoom), room.Id.ToString());
            if (locationId == location.Id)
                storedDestination = persisted;
        }
        if (storedDestination is null || location.TenantId != storedDestination.TenantId)
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The destination location changed. Reload and retry.", nameof(LocationRoom), room.Id.ToString());
        var originalLocationId = storedRoom.LocationId;
        _dbContext.DisclosureMutations.Enlist([tenantId]);
        _dbContext.Entry(room).State = EntityState.Detached;
        int affectedRows;
        try
        {
            affectedRows = await _dbContext.LocationRooms
                .Where(candidate =>
                    candidate.TenantId == tenantId
                    && candidate.Id == room.Id
                    && candidate.LocationId == originalLocationId
                    && !candidate.IsDeleted
                    && candidate.ConcurrencyStamp == expectedConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.LocationId, storedDestination.Id)
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

        room.LocationId = storedDestination.Id;
        room.Location = _dbContext.Locations.Local.SingleOrDefault(location => location.Id == storedDestination.Id)
            ?? storedDestination;
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
