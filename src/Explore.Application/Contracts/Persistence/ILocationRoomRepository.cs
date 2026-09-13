using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ILocationRoomRepository : IGenericRepository<LocationRoom, Guid>
{
    const int MaximumBatchSize = 256;

    Task<IReadOnlyList<LocationRoom>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);
    Task<List<LocationRoom>> GetByLocationAsync(Guid locationId, CancellationToken cancellationToken);
    Task<bool> HasScheduleReferencesAsync(Guid roomId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a room and applies its final name within the caller's transaction.
    /// The caller must save remaining changes to advance concurrency and audit fields.
    /// </summary>
    Task MoveToLocationAsync(
        LocationRoom room,
        Location location,
        string name,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken);
}
