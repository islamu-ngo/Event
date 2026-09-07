using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ILocationRoomRepository : IGenericRepository<LocationRoom, Guid>
{
    const int MaximumBatchSize = 256;

    Task<IReadOnlyList<LocationRoom>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);
    Task<List<LocationRoom>> GetByLocationAsync(Guid locationId, CancellationToken cancellationToken);
    Task<bool> HasActiveScheduleReferencesAsync(Guid roomId, CancellationToken cancellationToken);
}
