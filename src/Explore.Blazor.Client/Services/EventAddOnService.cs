using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Services;

public sealed class EventAddOnService(IBffClient bff) : IEventAddOnService
{
    public Task<HalResourceOfEventAddOnCatalogDto?> GetCatalogAsync(
        Guid eventId,
        CancellationToken cancellationToken) =>
        bff.GetAsync<HalResourceOfEventAddOnCatalogDto>(
            $"/bff/events/{eventId:D}/add-ons",
            cancellationToken);

    public Task<HalResourceOfEventAddOnCatalogDto?> GetManagementAsync(
        Guid eventId,
        CancellationToken cancellationToken) =>
        bff.GetAsync<HalResourceOfEventAddOnCatalogDto>(
            ManagementPath(eventId),
            cancellationToken);

    public Task<HalResourceOfEventAddOnCatalogDto?> CreateDraftAsync(
        string actionHref,
        string currencyCode,
        Guid operationId,
        CancellationToken cancellationToken) =>
        bff.SendIdempotentAsync<
            CreateEventAddOnCatalogDraftRequest,
            HalResourceOfEventAddOnCatalogDto>(
                HttpMethod.Post,
                NormalizeBffPath(actionHref),
                new CreateEventAddOnCatalogDraftRequest
                {
                    CurrencyCode = currencyCode,
                },
                operationId,
                cancellationToken);

    public Task<HalResourceOfEventAddOnCatalogDto?> AddItemAsync(
        string actionHref,
        ManageEventAddOnCatalogItemRequest request,
        Guid operationId,
        CancellationToken cancellationToken) =>
        bff.SendIdempotentAsync<
            ManageEventAddOnCatalogItemRequest,
            HalResourceOfEventAddOnCatalogDto>(
                HttpMethod.Post,
                NormalizeBffPath(actionHref),
                request,
                operationId,
                cancellationToken);

    public Task<HalResourceOfEventAddOnCatalogDto?> PublishAsync(
        string actionHref,
        Guid operationId,
        CancellationToken cancellationToken) =>
        bff.SendIdempotentAsync<HalResourceOfEventAddOnCatalogDto>(
            HttpMethod.Post,
            NormalizeBffPath(actionHref),
            operationId,
            cancellationToken);

    public Task<HalResourceOfEventAddOnCatalogDto?> RetireAsync(
        string actionHref,
        Guid operationId,
        CancellationToken cancellationToken) =>
        bff.SendIdempotentAsync<HalResourceOfEventAddOnCatalogDto>(
            HttpMethod.Post,
            NormalizeBffPath(actionHref),
            operationId,
            cancellationToken);

    public Task<HalResourceOfRegistrationOrderAddOnSummaryDto?> GetOrderAsync(
        Guid eventId,
        Guid registrationOrderId,
        string? capability,
        CancellationToken cancellationToken) =>
        bff.GetWithRegistrationOrderCapabilityAsync<
            HalResourceOfRegistrationOrderAddOnSummaryDto>(
                OrderPath(eventId, registrationOrderId),
                capability,
                cancellationToken);

    public Task<HalResourceOfRegistrationOrderAddOnSummaryDto?> ReserveAsync(
        string actionHref,
        Guid catalogId,
        IReadOnlyList<EventAddOnSelectionRequest> selections,
        Guid operationId,
        CancellationToken cancellationToken) =>
        bff.SendIdempotentAsync<
            ReserveEventAddOnsRequest,
            HalResourceOfRegistrationOrderAddOnSummaryDto>(
                HttpMethod.Post,
                NormalizeBffPath(actionHref),
                new ReserveEventAddOnsRequest
                {
                    CatalogId = catalogId,
                    Selections = selections.ToArray(),
                },
                operationId,
                cancellationToken);

    public Task<HalResourceOfRegistrationOrderAddOnSummaryDto?> FulfillAsync(
        string actionHref,
        Guid operationId,
        CancellationToken cancellationToken) =>
        bff.SendIdempotentAsync<HalResourceOfRegistrationOrderAddOnSummaryDto>(
            HttpMethod.Post,
            NormalizeBffPath(actionHref),
            operationId,
            cancellationToken);

    public Task<HalResourceOfRegistrationOrderAddOnSummaryDto?> RefundAsync(
        string actionHref,
        int quantity,
        Guid operationId,
        CancellationToken cancellationToken) =>
        bff.SendIdempotentAsync<
            RefundEventAddOnRequest,
            HalResourceOfRegistrationOrderAddOnSummaryDto>(
                HttpMethod.Post,
                NormalizeBffPath(actionHref),
                new RefundEventAddOnRequest { Quantity = quantity },
                operationId,
                cancellationToken);

    private static string OrderPath(Guid eventId, Guid registrationOrderId) =>
        $"/bff/events/{eventId:D}/registration-orders/" +
        $"{registrationOrderId:D}/add-ons";

    private static string ManagementPath(Guid eventId) =>
        $"/bff/events/{eventId:D}/add-ons/management";

    private static string NormalizeBffPath(string actionHref)
    {
        if (!Uri.TryCreate(actionHref, UriKind.RelativeOrAbsolute, out Uri? uri))
        {
            throw new ArgumentException("HAL action href is invalid.", nameof(actionHref));
        }

        string path = uri.IsAbsoluteUri
            ? uri.PathAndQuery
            : uri.OriginalString;
        if (path.StartsWith("/bff/", StringComparison.Ordinal))
        {
            return path;
        }

        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "HAL action href is outside the Event API boundary.",
                nameof(actionHref));
        }

        return $"/bff/{path["/api/".Length..]}";
    }
}
