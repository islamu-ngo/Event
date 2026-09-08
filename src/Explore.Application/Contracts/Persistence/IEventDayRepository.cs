using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventDayRepository : IGenericRepository<EventDay, Guid>
{
    Task<EventDay?> GetByIdForEventAsync(
        Guid eventDayId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<EventDay?> GetByIdForEventForUpdateAsync(
        Guid eventDayId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when the supplied day id belongs to the supplied event and is not soft-deleted.
    /// Used by registration workflow validation to reject a day scope that points at a foreign day.
    /// </summary>
    Task<bool> BelongsToEventAsync(Guid eventDayId, Guid eventId, CancellationToken cancellationToken);

    Task<List<EventDay>> GetByEventAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the EventDay for the given event whose LocalDate matches the supplied date, or null if none exists.
    /// Used by session handlers to auto-link EventSession.EventDayId after Reschedule computes LocalStartDate.
    /// </summary>
    Task<EventDay?> FindByEventAndLocalDateAsync(Guid eventId, DateOnly localDate, CancellationToken cancellationToken);
}
