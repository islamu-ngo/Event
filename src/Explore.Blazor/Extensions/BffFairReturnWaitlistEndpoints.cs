using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.Extensions;

public static class BffFairReturnWaitlistEndpoints
{
    private const string BasePath =
        "/bff/events/{eventId:guid}/" +
        "registration-orders/" +
        "{registrationOrderId:guid}/lines/" +
        "{registrationOrderLineId:guid}/waitlist";
    private const string CapabilityHeader =
        "X-Registration-Order-Capability";
    private const string IdempotencyHeader =
        "Idempotency-Key";

    public static WebApplication
        MapFairReturnWaitlistEndpoints(
            this WebApplication app)
    {
        app.MapGet(BasePath, HandleReadAsync);
        MapWrite(
            app.MapPost(BasePath, HandleJoinAsync));
        MapWrite(
            app.MapDelete(BasePath, HandleLeaveAsync));
        MapWrite(
            app.MapPost(
                $"{BasePath}/offers/" +
                "{offerId:guid}/accept",
                HandleAcceptAsync));
        MapWrite(
            app.MapDelete(
                $"{BasePath}/supply/" +
                "{supplyId:guid}",
                HandleWithdrawAsync));
        return app;
    }

    private static Task<IResult> HandleReadAsync(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        IFairReturnWaitlistClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.GetFairReturnWaitlistAsync(
                eventId,
                registrationOrderId,
                registrationOrderLineId,
                capability,
                cancellationToken:
                    cancellationToken),
            context);

    private static Task<IResult> HandleJoinAsync(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        [FromHeader(Name = IdempotencyHeader)]
        string idempotencyKey,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        IFairReturnWaitlistClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.JoinFairReturnWaitlistAsync(
                eventId,
                registrationOrderId,
                registrationOrderLineId,
                idempotencyKey,
                capability,
                cancellationToken:
                    cancellationToken),
            context);

    private static Task<IResult> HandleLeaveAsync(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        [FromHeader(Name = IdempotencyHeader)]
        string idempotencyKey,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        IFairReturnWaitlistClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.LeaveFairReturnWaitlistAsync(
                eventId,
                registrationOrderId,
                registrationOrderLineId,
                idempotencyKey,
                capability,
                cancellationToken:
                    cancellationToken),
            context);

    private static Task<IResult> HandleAcceptAsync(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        Guid offerId,
        [FromHeader(Name = IdempotencyHeader)]
        string idempotencyKey,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        IFairReturnWaitlistClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.AcceptFairReturnOfferAsync(
                eventId,
                registrationOrderId,
                registrationOrderLineId,
                offerId,
                idempotencyKey,
                capability,
                cancellationToken:
                    cancellationToken),
            context);

    private static Task<IResult> HandleWithdrawAsync(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        Guid supplyId,
        [FromHeader(Name = IdempotencyHeader)]
        string idempotencyKey,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        IFairReturnWaitlistClient api,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.WithdrawFairReturnSupplyAsync(
                eventId,
                registrationOrderId,
                registrationOrderLineId,
                supplyId,
                idempotencyKey,
                capability,
                cancellationToken:
                    cancellationToken),
            context);

    private static RouteHandlerBuilder MapWrite(
        RouteHandlerBuilder endpoint) =>
        endpoint
            .RequireAuthorization()
            .RequireRateLimiting(
                RateLimitingExtensions
                    .TicketTransferWritePolicy)
            .ValidateAntiforgeryBeforeRateLimiting();

    private static async Task<IResult>
        ForwardAsync<T>(
            Func<Task<T>> forward,
            HttpContext context)
    {
        context.Response.Headers.CacheControl =
            "private, no-store";
        try
        {
            return Results.Json(await forward());
        }
        catch (ApiException exception)
        {
            return exception.StatusCode is
                >= StatusCodes.Status400BadRequest
                and <= StatusCodes.Status429TooManyRequests
                ? Results.StatusCode(
                    exception.StatusCode)
                : Results.StatusCode(
                    StatusCodes.Status502BadGateway);
        }
    }
}
