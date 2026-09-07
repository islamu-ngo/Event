using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.Extensions;

public static class BffEventAddOnEndpoints
{
    private const string CatalogPath =
        "/bff/events/{eventId:guid}/add-ons";
    private const string ManagementPath =
        "/bff/events/{eventId:guid}/add-ons/management";
    private const string OrderPath =
        "/bff/events/{eventId:guid}/registration-orders/{registrationOrderId:guid}/add-ons";
    private const string CapabilityHeader =
        "X-Registration-Order-Capability";
    private const string IdempotencyHeader =
        "Idempotency-Key";
    private const string UnavailableCode =
        "event_add_on_unavailable";

    public static WebApplication MapEventAddOnBff(
        this WebApplication app)
    {
        app.MapGet(CatalogPath, GetCatalogAsync);
        app.MapGet(ManagementPath, GetManagementAsync)
            .RequireAuthorization();
        app.MapGet(OrderPath, GetOrderAsync);

        app.MapPost(
                $"{ManagementPath}/draft",
                CreateDraftAsync)
            .RequireAuthorization()
            .ValidateAntiforgeryBeforeRateLimiting();
        app.MapPost(
                $"{ManagementPath}/items",
                AddItemAsync)
            .RequireAuthorization()
            .ValidateAntiforgeryBeforeRateLimiting();
        app.MapPost(
                $"{ManagementPath}/publish",
                PublishAsync)
            .RequireAuthorization()
            .ValidateAntiforgeryBeforeRateLimiting();
        app.MapPost(
                $"{ManagementPath}/retire",
                RetireAsync)
            .RequireAuthorization()
            .ValidateAntiforgeryBeforeRateLimiting();
        app.MapPost(OrderPath, ReserveAsync)
            .RequireAuthorization()
            .ValidateAntiforgeryBeforeRateLimiting();
        app.MapPost(
                $"{OrderPath}/{{registrationOrderAddOnLineId:guid}}/fulfillment",
                FulfillAsync)
            .RequireAuthorization()
            .ValidateAntiforgeryBeforeRateLimiting();
        app.MapPost(
                $"{OrderPath}/{{registrationOrderAddOnLineId:guid}}/refunds",
                RefundAsync)
            .RequireAuthorization()
            .ValidateAntiforgeryBeforeRateLimiting();

        return app;
    }

    private static Task<IResult> GetCatalogAsync(
        Guid eventId,
        IEventAddOnCatalogClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.GetEventAddOnCatalogAsync(
                eventId,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> GetManagementAsync(
        Guid eventId,
        IEventAddOnManagementClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.GetEventAddOnManagementAsync(
                eventId,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> GetOrderAsync(
        Guid eventId,
        Guid registrationOrderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        IRegistrationOrderAddOnClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.GetRegistrationOrderAddOnsAsync(
                eventId,
                registrationOrderId,
                capability,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> CreateDraftAsync(
        Guid eventId,
        CreateEventAddOnCatalogDraftRequest body,
        [FromHeader(Name = IdempotencyHeader)] string idempotencyKey,
        IEventAddOnManagementClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.CreateEventAddOnCatalogDraftAsync(
                eventId,
                idempotencyKey,
                body,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> AddItemAsync(
        Guid eventId,
        ManageEventAddOnCatalogItemRequest body,
        [FromHeader(Name = IdempotencyHeader)] string idempotencyKey,
        IEventAddOnManagementClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.AddEventAddOnCatalogItemAsync(
                eventId,
                idempotencyKey,
                body,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> PublishAsync(
        Guid eventId,
        [FromHeader(Name = IdempotencyHeader)] string idempotencyKey,
        IEventAddOnManagementClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.PublishEventAddOnCatalogAsync(
                eventId,
                idempotencyKey,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> RetireAsync(
        Guid eventId,
        [FromHeader(Name = IdempotencyHeader)] string idempotencyKey,
        IEventAddOnManagementClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.RetireEventAddOnCatalogAsync(
                eventId,
                idempotencyKey,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> ReserveAsync(
        Guid eventId,
        Guid registrationOrderId,
        ReserveEventAddOnsRequest body,
        [FromHeader(Name = IdempotencyHeader)] string idempotencyKey,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        IRegistrationOrderAddOnClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.ReserveRegistrationOrderAddOnsAsync(
                eventId,
                registrationOrderId,
                idempotencyKey,
                body,
                capability,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> FulfillAsync(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderAddOnLineId,
        [FromHeader(Name = IdempotencyHeader)] string idempotencyKey,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        IRegistrationOrderAddOnClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.FulfillRegistrationOrderAddOnAsync(
                eventId,
                registrationOrderId,
                registrationOrderAddOnLineId,
                idempotencyKey,
                capability,
                cancellationToken: cancellationToken),
            context);

    private static Task<IResult> RefundAsync(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderAddOnLineId,
        RefundEventAddOnRequest body,
        [FromHeader(Name = IdempotencyHeader)] string idempotencyKey,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        IRegistrationOrderAddOnClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.RefundRegistrationOrderAddOnAsync(
                eventId,
                registrationOrderId,
                registrationOrderAddOnLineId,
                idempotencyKey,
                body,
                capability,
                cancellationToken: cancellationToken),
            context);

    private static async Task<IResult> ForwardAsync<T>(
        Func<Task<T>> forward,
        HttpContext context)
    {
        SetPrivateNoStore(context.Response.Headers);
        try
        {
            return Results.Json(await forward());
        }
        catch (ApiException exception)
        {
            return exception.StatusCode is
                >= StatusCodes.Status400BadRequest
                and <= StatusCodes.Status429TooManyRequests
                ? Results.Problem(
                    statusCode: exception.StatusCode,
                    title: "Add-on unavailable",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = UnavailableCode,
                    })
                : Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static void SetPrivateNoStore(IHeaderDictionary headers)
    {
        headers.CacheControl = "private, no-store";
        headers.Pragma = "no-cache";
        headers.Expires = "0";
    }
}
