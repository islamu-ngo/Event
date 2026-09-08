// ABOUTME: EventSession repository with friendly same-room overlap checks backed by a PostgreSQL exclusion constraint.
// ABOUTME: Validators use read checks, command writes re-check, and DB exclusion violations map to domain-facing conflicts.

using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Specifications.EventSessions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Repositories;

public class EventSessionRepository : GenericRepository<EventSession, Guid>, IEventSessionRepository
{
    private const string ExclusionViolationSqlState = "23P01";
    private readonly ExploreDbContext _dbContext;

    public EventSessionRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<EventSession?> GetByIdForEventAsync(
        Guid eventSessionId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        _dbContext.EventSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                session => session.Id == eventSessionId && session.EventId == eventId && session.TenantId == tenantId,
                cancellationToken);

    public async Task<EventSession?> GetByIdForEventForUpdateAsync(
        Guid eventSessionId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence.AcquireAsync<EventSession>(
            _dbContext,
            tenantId,
            session => session.Id,
            eventSessionId,
            cancellationToken);

        return await _dbContext.EventSessions.FirstOrDefaultAsync(
            session => session.Id == eventSessionId && session.EventId == eventId && session.TenantId == tenantId,
            cancellationToken);
    }

    public async Task<EventSession?> GetSessionWithDetails(Guid id)
    {
        return await _dbContext.EventSessions
            .AsNoTracking()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<EventSession?> GetPublicSessionWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await BuildPublicSessionQuery()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<List<EventSession>> GetSessionsByEvent(Guid eventId)
    {
        List<EventSession> sessions = await _dbContext.EventSessions
            .AsNoTracking()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .Where(s => s.EventId == eventId)
            .ToListAsync();

        return sessions.OrderBy(s => s.StartTime).ToList();
    }

    public async Task<List<EventSession>> GetPublicSessionsByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        List<EventSession> sessions = await BuildPublicSessionQuery()
            .Where(s => s.EventId == eventId)
            .ToListAsync(cancellationToken);

        return sessions.OrderBy(s => s.StartTime).ToList();
    }

    public async Task<List<EventSession>> GetSessionsByLocation(Guid locationId)
    {
        return await _dbContext.EventSessions
            .AsNoTracking()
            .Include(s => s.Event)
            .Include(s => s.RegistrationMode)
            .Include(s => s.IslamicAspect)
            .Where(s => s.LocationId == locationId)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<(List<EventSession> Items, int TotalCount)> GetSessionsWithDetailsPaged(int pageNumber, int pageSize)
    {
        var query = _dbContext.EventSessions
            .AsNoTracking()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .OrderByDescending(s => s.StartTime);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<(List<EventSession> Items, int TotalCount)> GetPublicSessionsWithDetailsPagedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = BuildPublicSessionQuery()
            .OrderByDescending(s => s.StartTime);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(List<EventSession> Items, int TotalCount)> GetSessionsWithDetailsPagedFiltered(
        int pageNumber, int pageSize, EventSessionQuerySpecification specification)
    {
        var query = _dbContext.EventSessions
            .AsNoTracking()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .AsQueryable();

        query = ApplySessionProjectionFilters(query, specification);
        query = specification.Apply(query);

        if (!specification.HasSort)
        {
            query = query.OrderByDescending(s => s.StartTime);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<(List<EventSession> Items, int TotalCount)> GetPublicSessionsWithDetailsPagedFilteredAsync(
        int pageNumber,
        int pageSize,
        EventSessionQuerySpecification specification,
        CancellationToken cancellationToken)
    {
        var query = BuildPublicSessionQuery();

        query = ApplySessionProjectionFilters(query, specification);
        query = specification.Apply(query);

        if (!specification.HasSort)
        {
            query = query.OrderByDescending(s => s.StartTime);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<EventSession>> GetOverlappingSessionsInRoomAsync(
        Guid roomId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        Guid? excludeSessionId,
        CancellationToken cancellationToken)
    {
        return await BuildOverlapQuery(roomId, startUtc, endUtc, excludeSessionId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public override async Task<EventSession> Create(EventSession entity)
    {
        try
        {
            return await base.Create(entity);
        }
        catch (DbUpdateException ex) when (IsRoomNoOverlapViolation(ex, entity.RoomId))
        {
            throw CreateRoomScheduleConflict(entity.RoomId!.Value);
        }
    }

    public override async Task Update(EventSession entity)
    {
        try
        {
            await base.Update(entity);
        }
        catch (DbUpdateException ex) when (IsRoomNoOverlapViolation(ex, entity.RoomId))
        {
            throw CreateRoomScheduleConflict(entity.RoomId!.Value);
        }
    }

    public async Task<EventSession> CreateWithRoomOverlapGuardAsync(
        EventSession session,
        CancellationToken cancellationToken)
    {
        // Null schedule skips overlap; the GiST exclusion is partial (NULL schedule exempt).
        if (session.RoomId is null || session.StartTime is null || session.EndTime is null)
        {
            return await Create(session);
        }

        if (_dbContext.Database.CurrentTransaction != null)
        {
            return await CreateWithRoomOverlapGuardInCurrentTransactionAsync(session, cancellationToken);
        }

        await using var tx = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            await CreateWithRoomOverlapGuardInCurrentTransactionAsync(session, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsRoomNoOverlapViolation(ex, session.RoomId))
        {
            await tx.RollbackAsync(cancellationToken);
            throw CreateRoomScheduleConflict(session.RoomId!.Value);
        }

        return session;
    }

    private async Task<EventSession> CreateWithRoomOverlapGuardInCurrentTransactionAsync(
        EventSession session,
        CancellationToken cancellationToken)
    {
        var conflicts = await BuildOverlapQuery(
                session.RoomId!.Value,
                session.StartTime!.Value,
                session.EndTime!.Value,
                excludeSessionId: null)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (conflicts.Count > 0)
        {
            throw new RoomScheduleConflictException(session.RoomId.Value, conflicts);
        }

        await _dbContext.EventSessions.AddAsync(session, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task UpdateWithRoomOverlapGuardAsync(
        EventSession session,
        CancellationToken cancellationToken)
    {
        if (session.RoomId is null || session.StartTime is null || session.EndTime is null)
        {
            await Update(session);
            return;
        }

        if (_dbContext.Database.CurrentTransaction != null)
        {
            await UpdateWithRoomOverlapGuardInCurrentTransactionAsync(session, cancellationToken);
            return;
        }

        await using var tx = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            await UpdateWithRoomOverlapGuardInCurrentTransactionAsync(session, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsRoomNoOverlapViolation(ex, session.RoomId))
        {
            await tx.RollbackAsync(cancellationToken);
            throw CreateRoomScheduleConflict(session.RoomId!.Value);
        }
    }

    public async Task MoveToEventAsync(
        EventSession session,
        Guid eventId,
        EventLocation eventLocation,
        Guid? roomId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Moving an event session requires an active transaction.");
        }

        _dbContext.Entry(session).State = EntityState.Detached;
        int affectedRows = await _dbContext.EventSessions
            .Where(candidate =>
                candidate.TenantId == session.TenantId &&
                candidate.Id == session.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.EventId, eventId)
                    .SetProperty(candidate => candidate.EventLocationId, eventLocation.Id)
                    .SetProperty(candidate => candidate.LocationId, eventLocation.LocationId)
                    .SetProperty(candidate => candidate.RoomId, roomId)
                    .SetProperty(candidate => candidate.EventDayId, (Guid?)null),
            cancellationToken);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException("The event session could not be moved because it is no longer active.");
        }

        session.EventId = eventId;
        session.EventDayId = null;
        session.AssignEventLocation(eventLocation);
        session.RoomId = roomId;
        _dbContext.EventSessions.Attach(session);
    }

    private async Task UpdateWithRoomOverlapGuardInCurrentTransactionAsync(
        EventSession session,
        CancellationToken cancellationToken)
    {
        var conflicts = await BuildOverlapQuery(session.RoomId!.Value, session.StartTime!.Value, session.EndTime!.Value, session.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (conflicts.Count > 0)
        {
            throw new RoomScheduleConflictException(session.RoomId.Value, conflicts);
        }

        var entry = _dbContext.Entry(session);
        if (entry.State == EntityState.Detached)
        {
            _dbContext.EventSessions.Attach(session);
            entry.State = EntityState.Modified;
        }
        else
        {
            entry.State = EntityState.Modified;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsRoomNoOverlapViolation(ex, session.RoomId))
        {
            throw CreateRoomScheduleConflict(session.RoomId!.Value);
        }
    }

    private bool IsRoomNoOverlapViolation(DbUpdateException ex, Guid? roomId)
    {
        return roomId.HasValue
            && ex.InnerException is PostgresException postgres
            && postgres.SqlState == ExclusionViolationSqlState
            && string.Equals(
                postgres.ConstraintName,
                RelationalConstraintDescriptorResolver.ExclusionConstraint<EventSession>(_dbContext),
                StringComparison.Ordinal);
    }

    private static RoomScheduleConflictException CreateRoomScheduleConflict(Guid roomId) =>
        new(roomId, Array.Empty<Guid>());

    private IQueryable<EventSession> BuildOverlapQuery(
        Guid roomId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        Guid? excludeSessionId)
    {
        var query = _dbContext.EventSessions
            .Where(s => s.RoomId == roomId)
            .Where(s => s.StartTime < endUtc && s.EndTime > startUtc);

        if (excludeSessionId.HasValue)
        {
            var excluded = excludeSessionId.Value;
            query = query.Where(s => s.Id != excluded);
        }

        return query;
    }

    private IQueryable<EventSession> BuildPublicSessionQuery()
    {
        return _dbContext.EventSessions
            .AsNoTracking()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .WherePubliclyEligible(_dbContext);
    }

    private IQueryable<EventSession> ApplySessionProjectionFilters(
        IQueryable<EventSession> query, EventSessionQuerySpecification specification)
    {
        foreach (var projFilter in specification.ProjectionFilters)
        {
            query = projFilter.FilterType switch
            {
                EventSessionCustomPropertyProjectionFilterType.ExactMatch => ApplyExactMatchFilter(query, projFilter),
                EventSessionCustomPropertyProjectionFilterType.OptionMatch => ApplyOptionMatchFilter(query, projFilter),
                EventSessionCustomPropertyProjectionFilterType.OptionsMatchAny => ApplyOptionsMatchAnyFilter(query, projFilter),
                EventSessionCustomPropertyProjectionFilterType.TextSearch => ApplyTextSearchFilter(query, projFilter),
                EventSessionCustomPropertyProjectionFilterType.GlobalTextSearch => ApplyGlobalTextSearchFilter(query, projFilter),
                EventSessionCustomPropertyProjectionFilterType.Exists => ApplyExistsFilter(query, projFilter),
                EventSessionCustomPropertyProjectionFilterType.BooleanTrue => ApplyBooleanTrueFilter(query, projFilter),
                EventSessionCustomPropertyProjectionFilterType.NumberRange => ApplyNumberRangeFilter(query, projFilter),
                EventSessionCustomPropertyProjectionFilterType.DateRange => ApplyDateRangeFilter(query, projFilter),
                _ => query
            };
        }

        return query;
    }

    private IQueryable<EventSession> ApplyExactMatchFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var (ns, key, normalizedValue) = ((string, string, string))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.NormalizedValue == normalizedValue));
    }

    private IQueryable<EventSession> ApplyOptionMatchFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var (ns, key, optionId) = ((string, string, Guid))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.OptionId == optionId));
    }

    private IQueryable<EventSession> ApplyOptionsMatchAnyFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var (ns, key, optionIds) = ((string, string, List<Guid>))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.OptionId != null &&
            optionIds.Contains(p.OptionId.Value)));
    }

    private IQueryable<EventSession> ApplyTextSearchFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var (ns, key, searchTerm) = ((string, string, string))filter.Value;
        var pattern = $"%{searchTerm.ToLowerInvariant()}%";
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsSearchable &&
            p.NormalizedValue != null &&
            EF.Functions.Like(p.NormalizedValue, pattern)));
    }

    private IQueryable<EventSession> ApplyGlobalTextSearchFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var searchTerm = (string)filter.Value;
        var pattern = $"%{searchTerm.ToLowerInvariant()}%";
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsSearchable &&
            p.NormalizedValue != null &&
            EF.Functions.Like(p.NormalizedValue, pattern)));
    }

    private IQueryable<EventSession> ApplyExistsFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var (ns, key) = ((string, string))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable));
    }

    private IQueryable<EventSession> ApplyBooleanTrueFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var (ns, key) = ((string, string))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.BooleanValue == true));
    }

    private IQueryable<EventSession> ApplyNumberRangeFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var (ns, key, min, max) = ((string, string, decimal?, decimal?))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.NumberValue != null &&
            (!min.HasValue || p.NumberValue >= min.Value) &&
            (!max.HasValue || p.NumberValue <= max.Value)));
    }

    private IQueryable<EventSession> ApplyDateRangeFilter(IQueryable<EventSession> query, EventSessionCustomPropertyProjectionFilter filter)
    {
        var (ns, key, from, to) = ((string, string, DateTimeOffset?, DateTimeOffset?))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(s => _dbContext.EventSessionCustomPropertyProjections.Any(p =>
            p.EventSessionId == s.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.DateTimeValue != null &&
            (!from.HasValue || p.DateTimeValue >= from.Value) &&
            (!to.HasValue || p.DateTimeValue <= to.Value)));
    }
}
