using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSessionGroupRepository : IGenericRepository<EventSessionGroup, Guid>
{
    Task<EventSessionGroup?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken);

    Task<EventSessionGroup?> GetPublicWithDetailsAsync(Guid id, CancellationToken cancellationToken);

    Task<EventSessionGroup?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<List<EventSessionGroup>> GetByEventAsync(Guid eventId, CancellationToken cancellationToken);

    Task<List<EventSessionGroup>> GetPublicByEventAsync(Guid eventId, CancellationToken cancellationToken);

    Task<List<EventSessionGroup>> GetActiveByEventAsync(Guid eventId, CancellationToken cancellationToken);
}
