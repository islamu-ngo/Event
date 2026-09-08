using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.Extensions;

public static class BffTicketPurchaseEndpoints
{
    private const string BasePath =
        "/bff/events/{eventId:guid}/registration-orders";
    private const string CapabilityHeader =
        "X-Registration-Order-Capability";
    private const int VerifiedContactAccessMode = 2;
    private const int NameOnlyAccessMode = 3;

    public static WebApplication MapTicketPurchaseEndpoints(
        this WebApplication app)
    {
        app.MapPost(
                $"{BasePath}/{{orderId:guid}}/purchase-authority",
                HandleAuthenticatedAsync)
            .RequireAuthorization()
            .RequireRateLimiting(
                RateLimitingExtensions
                    .TicketPurchaseAuthorityPolicy)
            .ValidateAntiforgeryBeforeRateLimiting();
        app.MapPost(
                $"{BasePath}/guest/{{orderId:guid}}/purchase-authority",
                HandleGuestAsync)
            .RequireRateLimiting(
                RateLimitingExtensions
                    .TicketPurchaseAuthorityPolicy)
            .ValidateAntiforgeryBeforeRateLimiting();
        return app;
    }

    private static async Task<IResult> HandleAuthenticatedAsync(
        Guid eventId,
        Guid orderId,
        ReserveTicketPurchaseRequest request,
        ITicketPurchaseClient api,
        CancellationToken cancellationToken)
    {
        if (!HasRequiredLineage(eventId, orderId))
        {
            return Results.BadRequest();
        }

        var body = new ReserveTicketPurchaseRequest
        {
            AccessMode = 1,
            RequestedPurchaserActorId =
                request.RequestedPurchaserActorId,
        };
        return await ForwardAsync(
            () => api.ReserveAuthenticatedPurchaseAuthorityAsync(
                eventId,
                orderId,
                CreateOperationKey(),
                body: body,
                cancellationToken: cancellationToken));
    }

    private static async Task<IResult> HandleGuestAsync(
        Guid eventId,
        Guid orderId,
        ReserveTicketPurchaseRequest request,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        ITicketPurchaseClient api,
        CancellationToken cancellationToken)
    {
        if (!HasRequiredLineage(eventId, orderId)
            || string.IsNullOrWhiteSpace(capability)
            || request.AccessMode is not (
                VerifiedContactAccessMode
                or NameOnlyAccessMode))
        {
            return Results.NotFound();
        }

        var body = new ReserveTicketPurchaseRequest
        {
            AccessMode = request.AccessMode,
            RequestedPurchaserActorId = null,
        };
        return await ForwardAsync(
            () => api.ReserveGuestPurchaseAuthorityAsync(
                eventId,
                orderId,
                CreateOperationKey(),
                capability,
                body: body,
                cancellationToken: cancellationToken));
    }

    private static async Task<IResult> ForwardAsync(
        Func<Task<
            HalResourceOfTicketPurchaseGovernanceResource>>
            forward)
    {
        try
        {
            HalResourceOfTicketPurchaseGovernanceResource
                response = await forward();
            return Results.Json(response);
        }
        catch (ApiException exception)
        {
            return exception.StatusCode is
                >= StatusCodes.Status400BadRequest
                and <= StatusCodes.Status429TooManyRequests
                ? Results.StatusCode(exception.StatusCode)
                : Results.StatusCode(
                    StatusCodes.Status502BadGateway);
        }
    }

    private static bool HasRequiredLineage(
        Guid eventId,
        Guid orderId) =>
        eventId != Guid.Empty
        && orderId != Guid.Empty;

    private static string CreateOperationKey() =>
        Guid.CreateVersion7().ToString("N");
}
