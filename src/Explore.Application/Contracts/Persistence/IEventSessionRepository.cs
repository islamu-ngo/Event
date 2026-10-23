using Explore.Application.Specifications.EventSessions;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSessionRepository : IGenericRepository<EventSession, Guid>
{
    Task<Event?> GetAuthorizedParentForMutationAsync(
        Guid tenantId, Guid userId, Guid eventId, string action, CancellationToken cancellationToken);

    Task<EventSession?> GetForMutationAsync(Guid id, CancellationToken cancellationToken);

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

    Task<EventSession?> GetSessionWithDetails(Guid id, CancellationToken cancellationToken = default);
    Task<EventSession?> GetPublicSessionWithDetailsAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Enumerates sessions through the current tenant filter and propagates caller cancellation.</summary>
    Task<List<EventSession>> GetSessionsByEvent(Guid eventId, CancellationToken cancellationToken = default);
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
    Task<EventSession> CreateWithRoomOverlapGuardAsync(EventSession session, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeMutation = null);

    /// <summary>
    /// Layer B update path: opens a Serializable transaction, re-checks same-room overlap (excluding
    /// the session being updated) inside the transaction, and commits only if no overlap exists.
    /// Throws <see cref="Explore.Application.Exceptions.RoomScheduleConflictException"/> on conflict.
    /// </summary>
    Task UpdateWithRoomOverlapGuardAsync(EventSession session, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeMutation = null);

    Task MoveToEventAsync(
        EventSession session,
        Guid eventId,
        EventLocation eventLocation,
        Guid? roomId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeMutation = null);
}
