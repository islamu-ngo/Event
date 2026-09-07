using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.Extensions;

public static class BffParticipantReadinessEndpoints
{
    private const string BasePath =
        "/bff/events/{eventId:guid}/participant-readiness/" +
        "registration-orders/{orderId:guid}/participants/" +
        "{participantId:guid}/assignments/{assignmentId:guid}";
    private const string CapabilityHeader =
        "X-Registration-Order-Capability";

    public static WebApplication
        MapParticipantReadinessEndpoints(
            this WebApplication app)
    {
        app.MapGet(BasePath, HandleReadAsync);
        MapWrite(
            app,
            $"{BasePath}/complete",
            static (
                api,
                eventId,
                orderId,
                participantId,
                assignmentId,
                cancellationToken) =>
                api.CompleteParticipantReadinessAsync(
                    eventId,
                    orderId,
                    participantId,
                    assignmentId,
                    cancellationToken: cancellationToken));
        MapWrite(
            app,
            $"{BasePath}/approve",
            static (
                api,
                eventId,
                orderId,
                participantId,
                assignmentId,
                cancellationToken) =>
                api.ApproveParticipantReadinessAsync(
                    eventId,
                    orderId,
                    participantId,
                    assignmentId,
                    cancellationToken: cancellationToken));
        MapWrite(
            app,
            $"{BasePath}/revoke",
            static (
                api,
                eventId,
                orderId,
                participantId,
                assignmentId,
                cancellationToken) =>
                api.RevokeParticipantReadinessAsync(
                    eventId,
                    orderId,
                    participantId,
                    assignmentId,
                    cancellationToken: cancellationToken));
        return app;
    }

    private static Task<IResult> HandleReadAsync(
        Guid eventId,
        Guid orderId,
        Guid participantId,
        Guid assignmentId,
        [FromHeader(Name = CapabilityHeader)]
        string? capability,
        IParticipantReadinessClient api,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            () => api.GetParticipantReadinessAsync(
                eventId,
                orderId,
                participantId,
                assignmentId,
                capability,
                cancellationToken: cancellationToken));

    private static void MapWrite(
        WebApplication app,
        string pattern,
        ReadinessForwarder forward)
    {
        app.MapPost(
                pattern,
                (
                    Guid eventId,
                    Guid orderId,
                    Guid participantId,
                    Guid assignmentId,
                    IParticipantReadinessClient api,
                    CancellationToken cancellationToken) =>
                    ForwardAsync(
                        () => forward(
                            api,
                            eventId,
                            orderId,
                            participantId,
                            assignmentId,
                            cancellationToken)))
            .RequireAuthorization()
            .RequireRateLimiting(
                RateLimitingExtensions
                    .ParticipantReadinessWritePolicy)
            .ValidateAntiforgeryBeforeRateLimiting();
    }

    private static async Task<IResult> ForwardAsync(
        Func<Task<HalResourceOfParticipantReadinessDto>>
            forward)
    {
        try
        {
            HalResourceOfParticipantReadinessDto response =
                await forward();
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

    private delegate Task<
        HalResourceOfParticipantReadinessDto>
        ReadinessForwarder(
            IParticipantReadinessClient api,
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken);
}
