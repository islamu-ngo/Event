using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventPublicActionRepository : IGenericRepository<EventPublicAction, Guid>
{
    /// <summary>
    /// Resolves current manage-public-actions authority under the caller's Serializable
    /// transaction. Holds contributing persisted rows through commit; returns no entity on denial.
    /// </summary>
    Task<Event?> GetAuthorizedEventForMutationAsync(
        Guid tenantId, Guid userId, Guid eventId, CancellationToken cancellationToken);

    Task<EventPublicAction?> GetDetailsAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    Task<EventPublicAction?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventPublicAction>> ListByEventAsync(Guid eventId, bool trackChanges, CancellationToken cancellationToken);
    Task<bool> HasOtherPrimaryAsync(Guid eventId, Guid? excludedActionId, CancellationToken cancellationToken);
}
