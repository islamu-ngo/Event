using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Preferences;

namespace Explore.Blazor.Extensions;

public static class BffSupportAccessEndpoints
{
    private const string HalJsonContentType = "application/hal+json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<SupportAccessModeEnum>() }
    };

    public static WebApplication MapSupportAccessEndpoints(this WebApplication app)
    {
        app.MapGet("/bff/support-access/current", HandleCurrentAsync)
            .RequireAuthorization()
            .ExcludeFromDescription();

        app.MapGet("/bff/support-access/tenants/{targetTenantId:guid}/sessions", HandleListSessionsAsync)
            .RequireAuthorization()
            .ExcludeFromDescription();

        app.MapGet("/bff/support-access/tenants/{targetTenantId:guid}/sessions/{sessionId:guid}/audit-events", HandleAuditEventsAsync)
            .RequireAuthorization()
            .ExcludeFromDescription();

        app.MapPost("/bff/support-access/sessions", HandleStartAsync)
            .RequireAuthorization()
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost("/bff/support-access/sessions/current/stop", HandleStopCurrentAsync)
            .RequireAuthorization()
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost("/bff/support-access/sessions/{sessionId:guid}/force-stop", HandleForceStopAsync)
            .RequireAuthorization()
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> HandleCurrentAsync(
        HttpContext ctx,
        ISupportAccessClient apiClient,
        IBffSupportAccessSessionStore sessionStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        CurrentSupportAccessSessionDto current;
        try
        {
            current = await apiClient.GetCurrentSupportAccessSessionAsync(cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            return BffForwardingResults.Problem(
                ex,
                "Could not resolve the current support-access session.",
                "Support access status failed");
        }

        var bindResult = await BindCurrentSessionAsync(
            ctx,
            sessionStore,
            current,
            loggerFactory,
            cancellationToken);
        if (bindResult is not null)
        {
            return bindResult;
        }

        return Results.Json(current, JsonOptions);
    }

    private static async Task<IResult> HandleListSessionsAsync(
        Guid targetTenantId,
        int limit,
        ISupportAccessClient apiClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessions = await apiClient.ListSupportAccessSessionsAsync(
                targetTenantId,
                ClampLimit(limit),
                cancellationToken: cancellationToken);
            return Results.Json(sessions, JsonOptions, contentType: HalJsonContentType);
        }
        catch (ApiException ex)
        {
            return BffForwardingResults.Problem(
                ex,
                "Could not load support-access sessions.",
                "Support access session history failed");
        }
    }

    private static async Task<IResult> HandleAuditEventsAsync(
        Guid targetTenantId,
        Guid sessionId,
        int limit,
        ISupportAccessClient apiClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditEvents = await apiClient.GetSupportAccessAuditEventsAsync(
                targetTenantId,
                sessionId,
                ClampLimit(limit),
                cancellationToken: cancellationToken);
            return Results.Json(auditEvents, JsonOptions, contentType: HalJsonContentType);
        }
        catch (ApiException ex)
        {
            return BffForwardingResults.Problem(
                ex,
                "Could not load support-access audit events.",
                "Support access audit failed");
        }
    }

    private static async Task<IResult> HandleStartAsync(
        StartSupportAccessSessionRequestDto? request,
        HttpContext ctx,
        ISupportAccessClient apiClient,
        IBffSupportAccessSessionStore sessionStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.Problem(
                detail: "Support-access start request body is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid support access request");
        }

        HalResourceOfSupportAccessSessionDto resource;
        try
        {
            resource = await apiClient.StartSupportAccessSessionAsync(
                request,
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            return BffForwardingResults.Problem(
                ex,
                "Could not start support access.",
                "Support access start failed");
        }

        var session = ToSession(resource);
        if (session is null)
        {
            return InvalidApiSessionResponse();
        }

        var storeResult = await sessionStore.StoreAsync(ctx.User, session, cancellationToken);
        if (!storeResult.Success)
        {
            loggerFactory.CreateLogger("SupportAccessBff").LogWarning(
                "Could not bind accepted support-access session to BFF store. FailureCode={FailureCode}",
                storeResult.FailureCode);
            return InvalidApiSessionResponse();
        }

        return Results.Json(resource, JsonOptions, contentType: HalJsonContentType);
    }

    private static async Task<IResult> HandleStopCurrentAsync(
        StopSupportAccessSessionRequestDto? request,
        HttpContext ctx,
        ISupportAccessClient apiClient,
        IBffSupportAccessSessionStore sessionStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var currentSession = await ResolveCurrentSessionAsync(
            ctx,
            apiClient,
            sessionStore,
            loggerFactory,
            cancellationToken);
        if (currentSession is null)
        {
            return Results.Problem(
                detail: "No active support-access session is available to stop.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Support access is not active");
        }

        if (currentSession.Id is not Guid currentSessionId || currentSessionId == Guid.Empty)
        {
            return InvalidApiSessionResponse();
        }

        HalResourceOfSupportAccessSessionDto resource;
        try
        {
            resource = await apiClient.StopSupportAccessSessionAsync(
                currentSessionId,
                request ?? new StopSupportAccessSessionRequestDto(),
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            return BffForwardingResults.Problem(
                ex,
                "Could not stop support access.",
                "Support access stop failed");
        }

        if (resource.Id is null)
        {
            await sessionStore.ClearAsync(ctx.User, cancellationToken);
            return InvalidApiSessionResponse();
        }

        await sessionStore.ClearAsync(ctx.User, cancellationToken);
        return Results.Json(resource, JsonOptions, contentType: HalJsonContentType);
    }

    private static async Task<IResult> HandleForceStopAsync(
        Guid sessionId,
        ForceStopSupportAccessSessionRequestDto? request,
        HttpContext ctx,
        ISupportAccessClient apiClient,
        IBffSupportAccessSessionStore sessionStore,
        CancellationToken cancellationToken)
    {
        HalResourceOfSupportAccessSessionDto resource;
        try
        {
            resource = await apiClient.ForceStopSupportAccessSessionAsync(
                sessionId,
                request ?? new ForceStopSupportAccessSessionRequestDto(),
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            return BffForwardingResults.Problem(
                ex,
                "Could not force-stop support access.",
                "Support access force-stop failed");
        }

        var cached = await sessionStore.ResolveCurrentAsync(cancellationToken);
        if (cached.Success && cached.Session?.SessionId == sessionId)
        {
            await sessionStore.ClearAsync(ctx.User, cancellationToken);
        }

        return resource.Id is null
            ? InvalidApiSessionResponse()
            : Results.Json(resource, JsonOptions, contentType: HalJsonContentType);
    }

    private static async Task<SupportAccessSessionDto?> ResolveCurrentSessionAsync(
        HttpContext ctx,
        ISupportAccessClient apiClient,
        IBffSupportAccessSessionStore sessionStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var cached = await sessionStore.ResolveCurrentAsync(cancellationToken);
        if (cached.Success && cached.Session is not null)
        {
            return new SupportAccessSessionDto
            {
                Id = cached.Session.SessionId,
                TargetTenantId = cached.Session.TargetTenantId,
                ModeId = cached.Session.ModeId,
                AllowsWrites = cached.Session.AllowsWrites,
                ExpiresAtUtc = cached.Session.ExpiresAtUtc,
                IsActive = true
            };
        }

        CurrentSupportAccessSessionDto current;
        try
        {
            current = await apiClient.GetCurrentSupportAccessSessionAsync(cancellationToken: cancellationToken);
        }
        catch (ApiException)
        {
            return null;
        }

        var bindResult = await BindCurrentSessionAsync(
            ctx,
            sessionStore,
            current,
            loggerFactory,
            cancellationToken);

        return bindResult is null && current.IsActive == true
            ? current.Session
            : null;
    }

    private static async Task<IResult?> BindCurrentSessionAsync(
        HttpContext ctx,
        IBffSupportAccessSessionStore sessionStore,
        CurrentSupportAccessSessionDto? current,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (current?.IsActive != true || current.Session is null)
        {
            await sessionStore.ClearAsync(ctx.User, cancellationToken);
            return null;
        }

        var storeResult = await sessionStore.StoreAsync(ctx.User, current.Session, cancellationToken);
        if (storeResult.Success)
        {
            return null;
        }

        loggerFactory.CreateLogger("SupportAccessBff").LogWarning(
            "Could not bind current support-access session to BFF store. FailureCode={FailureCode}",
            storeResult.FailureCode);
        return InvalidApiSessionResponse();
    }

    private static SupportAccessSessionDto? ToSession(HalResourceOfSupportAccessSessionDto resource)
    {
        if (resource.Id is not Guid id || id == Guid.Empty)
        {
            return null;
        }

        return new SupportAccessSessionDto
        {
            Id = id,
            TargetTenantId = resource.TargetTenantId,
            ModeId = resource.ModeId,
            AllowsWrites = resource.AllowsWrites,
            ExpiresAtUtc = resource.ExpiresAtUtc,
            IsActive = resource.IsActive
        };
    }

    private static IResult InvalidApiSessionResponse()
    {
        return Results.Problem(
            detail: "Support-access API returned an invalid session response.",
            statusCode: StatusCodes.Status502BadGateway,
            title: "Invalid support access response");
    }

    private static int ClampLimit(int limit) => Math.Clamp(limit <= 0 ? 100 : limit, 1, 250);
}
