using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSessionSpeakerRepository : IGenericRepository<EventSessionSpeaker, Guid>
{
    Task<List<EventSessionSpeaker>> GetBySession(Guid eventSessionId, CancellationToken cancellationToken = default);
    Task<List<EventSessionSpeaker>> GetByActor(Guid actorId, CancellationToken cancellationToken = default);
    Task<(List<EventSessionSpeaker> Items, int TotalCount)> GetSpeakersWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<EventSessionSpeaker?> GetBySessionAndActor(
        Guid eventSessionId,
        Guid actorId,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);
}
