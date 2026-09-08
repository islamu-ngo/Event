using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IEventAddOnService
{
    Task<HalResourceOfEventAddOnCatalogDto?> GetCatalogAsync(
        Guid eventId,
        CancellationToken cancellationToken);

    Task<HalResourceOfEventAddOnCatalogDto?> GetManagementAsync(
        Guid eventId,
        CancellationToken cancellationToken);

    Task<HalResourceOfEventAddOnCatalogDto?> CreateDraftAsync(
        string actionHref,
        string currencyCode,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<HalResourceOfEventAddOnCatalogDto?> AddItemAsync(
        string actionHref,
        ManageEventAddOnCatalogItemRequest request,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<HalResourceOfEventAddOnCatalogDto?> PublishAsync(
        string actionHref,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<HalResourceOfEventAddOnCatalogDto?> RetireAsync(
        string actionHref,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<HalResourceOfRegistrationOrderAddOnSummaryDto?> GetOrderAsync(
        Guid eventId,
        Guid registrationOrderId,
        string? capability,
        CancellationToken cancellationToken);

    Task<HalResourceOfRegistrationOrderAddOnSummaryDto?> ReserveAsync(
        string actionHref,
        Guid catalogId,
        IReadOnlyList<EventAddOnSelectionRequest> selections,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<HalResourceOfRegistrationOrderAddOnSummaryDto?> FulfillAsync(
        string actionHref,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<HalResourceOfRegistrationOrderAddOnSummaryDto?> RefundAsync(
        string actionHref,
        int quantity,
        Guid operationId,
        CancellationToken cancellationToken);
}
