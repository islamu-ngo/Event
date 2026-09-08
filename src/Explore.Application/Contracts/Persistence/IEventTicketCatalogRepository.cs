using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventTicketCatalogRepository
{
    Task<EventTicketCatalogVersion?> GetManagementCatalogAsync(Guid eventId, Guid tenantId, CancellationToken cancellationToken);

    Task<EventTicketCatalogVersion?> GetPublishedCatalogAsync(Guid eventId, Guid tenantId, CancellationToken cancellationToken);

    Task<EventTicketCatalogVersion?> GetOrderCatalogAsync(
        Guid catalogId,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<EventTicketCatalogVersion?> GetDraftCatalogForUpdateAsync(Guid eventId, Guid tenantId, CancellationToken cancellationToken);

    Task<EventTicketCatalogVersion?> GetPublishedForUpdateAsync(Guid eventId, Guid tenantId, CancellationToken cancellationToken);

    Task<EventTicketCatalogVersion?> GetByEventVersionAndTenantAsync(Guid eventId, int versionNumber, Guid tenantId, CancellationToken cancellationToken);

    Task<EventTicketType?> GetTicketTypeByIdEventAndTenantAsync(Guid ticketTypeId, Guid eventId, Guid tenantId, CancellationToken cancellationToken);

    Task<EventCapacityPool?> GetCapacityPoolByIdEventAndTenantAsync(Guid capacityPoolId, Guid eventId, Guid tenantId, CancellationToken cancellationToken);

    Task<EventCapacityPool?> GetActiveCapacityPoolForUpdateAsync(Guid capacityPoolId, Guid eventId, Guid tenantId, CancellationToken cancellationToken);

    Task<bool> HasLiveTicketTypeReferencesAsync(Guid capacityPoolId, Guid eventId, Guid tenantId, CancellationToken cancellationToken);

    Task AddAsync(EventTicketCatalogVersion catalog, CancellationToken cancellationToken);

    Task UpdateAsync(EventTicketCatalogVersion catalog, CancellationToken cancellationToken);

    Task RemoveEntitlementsAsync(IEnumerable<TicketTypeEntitlement> entitlements, CancellationToken cancellationToken);

    Task AddCapacityPoolAsync(EventCapacityPool pool, CancellationToken cancellationToken);

    Task UpdateCapacityPoolAsync(EventCapacityPool pool, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
