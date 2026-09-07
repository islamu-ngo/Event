using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventPublicActionRepository : IGenericRepository<EventPublicAction, Guid>
{
    Task<EventPublicAction?> GetDetailsAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    Task<EventPublicAction?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventPublicAction>> ListByEventAsync(Guid eventId, bool trackChanges, CancellationToken cancellationToken);
    Task<bool> HasOtherPrimaryAsync(Guid eventId, Guid? excludedActionId, CancellationToken cancellationToken);
}
