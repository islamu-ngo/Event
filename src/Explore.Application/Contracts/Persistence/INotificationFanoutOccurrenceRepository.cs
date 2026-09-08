using Explore.Application.Models.InternalEvents;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface INotificationFanoutOccurrenceRepository : IGenericRepository<NotificationFanoutOccurrence, Guid>
{
    Task<bool> AcquireEventPrecedenceLockAndHasHeavyAuthorityAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task AcquireSourceThenEventCoordinationLocksAsync(
        Guid tenantId,
        string sourceType,
        Guid sourceId,
        Guid aggregateVersion,
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task<NotificationFanoutOccurrence?> GetBySourceIdentityForCoordinationAsync(
        Guid tenantId,
        string sourceType,
        Guid sourceId,
        Guid aggregateVersion,
        CancellationToken cancellationToken = default);

    Task<bool> SessionBelongsToEventForCoordinationAsync(
        Guid tenantId,
        Guid eventId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<NotificationFanoutOccurrence?> GetByIdForCoordinationAsync(
        Guid tenantId,
        Guid occurrenceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationFanoutOccurrence>> GetPendingForEventCoordinationAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationFanoutOccurrence>> GetDirectPredecessorsForCoordinationAsync(
        Guid tenantId,
        Guid eventId,
        Guid replacementOccurrenceId,
        CancellationToken cancellationToken = default);

    Task<bool> TryPersistSupersessionAsync(
        NotificationFanoutOccurrence occurrence,
        CancellationToken cancellationToken = default);

    Task<int> SettleNonTerminalRunsForSupersededOccurrenceAsync(
        Guid tenantId,
        Guid occurrenceId,
        DateTime settledAt,
        CancellationToken cancellationToken = default);

    Task<NotificationFanoutOccurrence?> GetByPointerAsync(
        NotificationFanoutOccurrenceRequested pointer,
        bool trackChanges = false,
        CancellationToken cancellationToken = default);
}
