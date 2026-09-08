using Explore.Application.Specifications.EventSessions;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSessionRepository : IGenericRepository<EventSession, Guid>
{
    Task<EventSession?> GetByIdForEventAsync(
        Guid eventSessionId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<EventSession?> GetByIdForEventForUpdateAsync(
        Guid eventSessionId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<EventSession?> GetSessionWithDetails(Guid id);
    Task<EventSession?> GetPublicSessionWithDetailsAsync(Guid id, CancellationToken cancellationToken);
    Task<List<EventSession>> GetSessionsByEvent(Guid eventId);
    Task<List<EventSession>> GetPublicSessionsByEventAsync(Guid eventId, CancellationToken cancellationToken);
    Task<List<EventSession>> GetSessionsByLocation(Guid locationId);
    Task<(List<EventSession> Items, int TotalCount)> GetSessionsWithDetailsPaged(int pageNumber, int pageSize);
    Task<(List<EventSession> Items, int TotalCount)> GetPublicSessionsWithDetailsPagedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    Task<(List<EventSession> Items, int TotalCount)> GetSessionsWithDetailsPagedFiltered(int pageNumber, int pageSize, EventSessionQuerySpecification specification);
    Task<(List<EventSession> Items, int TotalCount)> GetPublicSessionsWithDetailsPagedFilteredAsync(
        int pageNumber,
        int pageSize,
        EventSessionQuerySpecification specification,
        CancellationToken cancellationToken);

    /// <summary>
    /// Layer A read used by CreateEventSessionDtoValidator/UpdateEventSessionDtoValidator.
    /// Returns persisted (non-deleted) sessions in the given room whose UTC time range overlaps
    /// [startUtc, endUtc), optionally excluding a session being updated.
    /// </summary>
    Task<IReadOnlyList<EventSession>> GetOverlappingSessionsInRoomAsync(
        Guid roomId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        Guid? excludeSessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Layer B create path: reuses an ambient transaction or opens a Serializable transaction,
    /// re-checks same-room overlap, and commits only if no overlap exists. Throws
    /// <see cref="Explore.Application.Exceptions.RoomScheduleConflictException"/> on conflict.
    /// If <see cref="EventSession.RoomId"/> is null, falls back to the base Create path.
    /// </summary>
    Task<EventSession> CreateWithRoomOverlapGuardAsync(EventSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Layer B update path: opens a Serializable transaction, re-checks same-room overlap (excluding
    /// the session being updated) inside the transaction, and commits only if no overlap exists.
    /// Throws <see cref="Explore.Application.Exceptions.RoomScheduleConflictException"/> on conflict.
    /// </summary>
    Task UpdateWithRoomOverlapGuardAsync(EventSession session, CancellationToken cancellationToken);

    Task MoveToEventAsync(
        EventSession session,
        Guid eventId,
        EventLocation eventLocation,
        Guid? roomId,
        CancellationToken cancellationToken);
}
