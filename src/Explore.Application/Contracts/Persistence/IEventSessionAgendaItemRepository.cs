using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSessionAgendaItemRepository : IGenericRepository<EventSessionAgendaItem, Guid>
{
    Task<EventSessionAgendaItem?> GetByIdWithDetails(Guid id, CancellationToken cancellationToken = default);
    Task<EventSessionAgendaItem?> GetPublicByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<EventSessionAgendaItem>> GetBySession(Guid eventSessionId, CancellationToken cancellationToken = default);
    Task<List<EventSessionAgendaItem>> GetPublicBySessionAsync(Guid eventSessionId, CancellationToken cancellationToken = default);
    Task<(List<EventSessionAgendaItem> Items, int TotalCount)> GetAgendaItemsWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<(List<EventSessionAgendaItem> Items, int TotalCount)> GetPublicAgendaItemsWithDetailsPagedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}
