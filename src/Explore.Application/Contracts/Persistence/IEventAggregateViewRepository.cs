using Explore.Domain;
using Explore.Domain.Views;

namespace Explore.Application.Contracts.Persistence;

public interface IEventAggregateViewRepository
{
    Task<EventWithSessionsView?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken);

    Task<(List<EventWithSessionsView> Items, int TotalCount)> GetPagedAsync(
        EventAggregateViewFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    Task<List<EventCustomPropertyDefinition>> GetEventDefinitionsByEventIdsAsync(
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken);

    Task<List<EventSessionCustomPropertyDefinition>> GetSessionDefinitionsForEventAsync(
        Guid eventId,
        CancellationToken cancellationToken);
}
