using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.Extensions;

public static class BffTicketTransferEndpoints
{
    private const string BasePath =
        "/bff/events/{eventId:guid}/admission-tickets/" +
        "{admissionTicketId:guid}/transfers";
    private const string ItemPath =
        $"{BasePath}/{{transferId:guid}}";
    private const string CapabilityHeader =
        "X-Ticket-Transfer-Capability";

    public static WebApplication MapTicketTransferEndpoints(
        this WebApplication app)
    {
        app.MapGet(ItemPath, HandleReadAsync);
        MapWrite(
            app.MapPost(BasePath, HandleOfferAsync));
        MapWrite(
            app.MapPost(
                $"{ItemPath}/accept",
                HandleAcceptAsync));
        MapWrite(
            app.MapDelete(
                ItemPath,
                HandleCancelAsync));
        MapWrite(
            app.MapPost(
                $"{ItemPath}/correction",
                HandleCorrectionAsync));
        MapWrite(
            app.MapPost(
                $"{ItemPath}/reissue",
                HandleReissueAsync));
        return app;
    }

    private static Task<IResult> HandleReadAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        ITicketTransferClient api,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.GetTicketTransferAsync(
                eventId,
                admissionTicketId,
                transferId,
                capability,
                cancellationToken: cancellationToken));

    private static Task<IResult> HandleOfferAsync(
        Guid eventId,
        Guid admissionTicketId,
        ITicketTransferClient api,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.OfferTicketTransferAsync(
                eventId,
                admissionTicketId,
                cancellationToken: cancellationToken));

    private static Task<IResult> HandleAcceptAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        AcceptTicketTransferRequest request,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        ITicketTransferClient api,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.AcceptTicketTransferAsync(
                eventId,
                admissionTicketId,
                transferId,
                request,
                capability,
                cancellationToken: cancellationToken));

    private static Task<IResult> HandleCancelAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        ITicketTransferClient api,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.CancelTicketTransferAsync(
                eventId,
                admissionTicketId,
                transferId,
                cancellationToken: cancellationToken));

    private static Task<IResult> HandleCorrectionAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        ITicketTransferClient api,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.CorrectTicketTransferAsync(
                eventId,
                admissionTicketId,
                transferId,
                cancellationToken: cancellationToken));

    private static Task<IResult> HandleReissueAsync(
        Guid eventId,
        Guid admissionTicketId,
        Guid transferId,
        ITicketTransferClient api,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.ReissueTransferredTicketAsync(
                eventId,
                admissionTicketId,
                transferId,
                cancellationToken: cancellationToken));

    private static RouteHandlerBuilder MapWrite(
        RouteHandlerBuilder endpoint) =>
        endpoint
            .RequireAuthorization()
            .RequireRateLimiting(
                RateLimitingExtensions
                    .TicketTransferWritePolicy)
            .ValidateAntiforgeryBeforeRateLimiting();

    private static async Task<IResult> ForwardAsync<T>(
        Func<Task<T>> forward)
    {
        try
        {
            T response = await forward();
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
}
