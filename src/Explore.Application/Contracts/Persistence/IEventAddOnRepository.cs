using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventAddOnRepository
{
    Task<EventAddOnCatalogVersion?> GetPublishedCatalogAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<EventAddOnCatalogVersion?> GetPublishedCatalogByIdAsync(
        Guid tenantId,
        Guid eventId,
        Guid catalogId,
        CancellationToken cancellationToken);

    Task<EventAddOnCatalogVersion?> GetManagementCatalogAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<EventAddOnCatalogVersion?> GetDraftCatalogForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<EventAddOnCatalogVersion?> GetPublishedCatalogForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task AddCatalogAsync(
        EventAddOnCatalogVersion catalog,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> GetAvailableCatalogItemQuantitiesAsync(
        Guid tenantId,
        Guid eventId,
        Guid catalogId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventAddOnFulfillment>> ListFulfillmentsAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventAddOnRefundAllocation>> ListRefundAllocationsAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<RegistrationOrder?> GetOrderForAddOnUpdateAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<EventAddOnInventoryResult> ReserveInventoryAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderAddOnLineId,
        Guid operationId,
        DateTime reservedAtUtc,
        CancellationToken cancellationToken);

    Task<EventAddOnFulfillmentResult> FulfillAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderAddOnLineId,
        Guid operationId,
        DateTime fulfilledAtUtc,
        CancellationToken cancellationToken);

    Task<EventAddOnRefundResult> AllocateRefundAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderAddOnLineId,
        Guid refundOperationId,
        int quantity,
        DateTime allocatedAtUtc,
        CancellationToken cancellationToken);

    Task<EventAddOnRefundAllocation?> ResolveRefundAsync(
        Guid tenantId,
        Guid refundOperationId,
        bool providerSucceeded,
        DateTime resolvedAtUtc,
        CancellationToken cancellationToken);

    Task<RegistrationOrder?> GetOrderWithAddOnsAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);
}
