// ABOUTME: Setup-secret BFF endpoints: set, sync, and delete setup secrets.
// ABOUTME: Includes validation against the API, cookie management, and session persistence.

using System.Net;
using System.Text.Json;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.Extensions;

public static class BffSetupSecretEndpoints
{
    private const int MaxSetupSecretLength = 512;
    private static readonly TimeSpan SetupSessionLifetime = TimeSpan.FromMinutes(30);
    private const string SetupSecretCookieName = "setup-secret";
    private const string SetupSecretSessionCookieName = "setup-secret-session";
    private const string SetupSecretRequiredDetail = "Setup secret is required.";
    private const string InvalidSetupSecretDetail = "Invalid setup secret.";
    private const string SetupAlreadyCompletedDetail = "Setup is already completed.";
    private const string TooManyAttemptsDetail = "Too many setup-secret attempts. Please wait and try again.";
    private const string ValidationUnavailableDetail = "Could not validate setup secret at this time.";
    private const string ValidationFailedDetail = "Setup secret validation failed.";

    /// <summary>
    /// Maps setup-secret endpoints: POST /bff/setup-secret, POST /bff/setup-secret/sync, DELETE /bff/setup-secret.
    /// </summary>
    public static WebApplication MapSetupSecretEndpoints(this WebApplication app)
    {
        app.MapGet("/bff/setup-secret", HandleGetSetupSecretStatusAsync)
            .ExcludeFromDescription();

        app.MapPost("/bff/setup-secret", HandleSetupSecretAsync)
            .ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.SetupSecretPolicy)
            .ExcludeFromDescription();

        app.MapPost("/bff/setup-secret/sync", HandleSetupSecretSyncAsync)
            .ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.SetupSecretPolicy)
            .ExcludeFromDescription();

        app.MapDelete("/bff/setup-secret", HandleDeleteSetupSecret)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        return app;
    }

    // ──────────────────────────────────────────────
    // Endpoint handlers
    // ──────────────────────────────────────────────

    // NOTE: Handlers taking only HttpContext and returning Task<IResult> are silently
    // coerced to RequestDelegate (Func<HttpContext, Task>) by the minimal API factory,
    // causing IResult.ExecuteAsync to never run. We use direct response writing instead.
    private static async Task HandleGetSetupSecretStatusAsync(HttpContext ctx)
    {
        var sessionService = (ISetupSecretSessionService)ctx.RequestServices.GetRequiredService<SetupSecretSessionService>();
        var secretResolver = ctx.RequestServices.GetRequiredService<ISetupSecretResolver>();
        var resolution = secretResolver.Resolve(ctx);
        var secret = resolution.Found ? resolution.Secret?.Trim() : null;

        if (string.IsNullOrWhiteSpace(secret))
        {
            await ctx.Response.WriteAsJsonAsync(new SetupSecretStatusResponse());
            return;
        }

        var validation = IsTrustedPersistedSource(resolution.Source)
            ? new SetupSecretValidationResult(true, StatusCodes.Status200OK, string.Empty)
            : await ValidateSetupSecretAsync(ctx, secret, ctx.RequestAborted);
        if (!validation.IsValid)
        {
            if (IsPermanentValidationFailure(validation.StatusCode))
            {
                ClearSetupSecret(ctx, sessionService, ShouldUseSecureSetupCookie(ctx));
                await ctx.Response.WriteAsJsonAsync(new SetupSecretStatusResponse
                {
                    HasPersistedSecret = false,
                    IsValid = false,
                    Error = validation.Error
                });
                return;
            }

            await ctx.Response.WriteAsJsonAsync(new SetupSecretStatusResponse
            {
                HasPersistedSecret = true,
                IsValid = false,
                Error = validation.Error
            });
            return;
        }

        PersistSetupSecret(ctx, sessionService, secret, ShouldUseSecureSetupCookie(ctx));
        await ctx.Response.WriteAsJsonAsync(new SetupSecretStatusResponse
        {
            HasPersistedSecret = true,
            IsValid = true
        });
    }

    private static async Task HandleSetupSecretAsync(HttpContext ctx)
    {
        var sessionService = (ISetupSecretSessionService)ctx.RequestServices.GetRequiredService<SetupSecretSessionService>();
        var request = await ReadSetupSecretRequestAsync(ctx);
        if (!request.IsValid)
        {
            await WriteProblemAsync(ctx, StatusCodes.Status400BadRequest, "Bad Request", request.Error);
            return;
        }

        var secret = request.Secret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            await WriteProblemAsync(ctx, StatusCodes.Status400BadRequest, "Bad Request", SetupSecretRequiredDetail);
            return;
        }

        var validation = await ValidateSetupSecretAsync(ctx, secret, ctx.RequestAborted);
        if (!validation.IsValid)
        {
            if (IsPermanentValidationFailure(validation.StatusCode))
            {
                ClearSetupSecret(ctx, sessionService, ShouldUseSecureSetupCookie(ctx));
            }

            await WriteProblemAsync(ctx, validation.StatusCode, "Setup Secret Validation Failed", validation.Error);
            return;
        }

        PersistSetupSecret(ctx, sessionService, secret, ShouldUseSecureSetupCookie(ctx));
    }

    private static async Task HandleSetupSecretSyncAsync(HttpContext ctx)
    {
        var sessionService = (ISetupSecretSessionService)ctx.RequestServices.GetRequiredService<SetupSecretSessionService>();
        var request = await ReadSetupSecretRequestAsync(ctx);
        if (!request.IsValid)
        {
            await WriteProblemAsync(ctx, StatusCodes.Status400BadRequest, "Bad Request", request.Error);
            return;
        }

        var userId = ResolveUserId(ctx);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var secret = request.Secret;
        var requiresValidation = true;
        if (string.IsNullOrWhiteSpace(secret))
        {
            var secretResolver = ctx.RequestServices.GetRequiredService<ISetupSecretResolver>();
            var resolution = secretResolver.Resolve(ctx);
            secret = resolution.Found ? resolution.Secret?.Trim() : null;
            requiresValidation = !IsTrustedPersistedSource(resolution.Source);
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            await WriteProblemAsync(ctx, StatusCodes.Status400BadRequest, "Bad Request", SetupSecretRequiredDetail);
            return;
        }

        if (requiresValidation)
        {
            var validation = await ValidateSetupSecretAsync(ctx, secret, ctx.RequestAborted);
            if (!validation.IsValid)
            {
                if (IsPermanentValidationFailure(validation.StatusCode))
                {
                    ClearSetupSecret(ctx, sessionService, ShouldUseSecureSetupCookie(ctx), userId);
                }

                await WriteProblemAsync(ctx, validation.StatusCode, "Setup Secret Validation Failed", validation.Error);
                return;
            }
        }

        PersistSetupSecret(ctx, sessionService, secret, ShouldUseSecureSetupCookie(ctx), userId);
    }

    // Permanent failures invalidate the persisted secret (user must re-enter):
    //   - 403 Forbidden: secret is wrong
    //   - 410 Gone: onboarding already completed, secret no longer applicable
    // Transient failures (429, 502, 503, connection errors) keep the secret so the
    // user doesn't need to re-enter it when the API briefly misbehaves.
    private static bool IsPermanentValidationFailure(int statusCode) =>
        statusCode is StatusCodes.Status403Forbidden or StatusCodes.Status410Gone;

    private static IResult HandleDeleteSetupSecret(HttpContext ctx)
    {
        var sessionService = (ISetupSecretSessionService)ctx.RequestServices.GetRequiredService<SetupSecretSessionService>();
        ClearSetupSecret(ctx, sessionService, ShouldUseSecureSetupCookie(ctx));
        return Results.Ok();
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static string? ResolveUserId(HttpContext ctx)
    {
        return ctx.User.TryGetSetupSessionIdentity(out var identity) ? identity.PartitionKey : null;
    }

    private static bool IsTrustedPersistedSource(SetupSecretSource source) =>
        source is SetupSecretSource.ServerSideSetupSession
            or SetupSecretSource.AnonymousSetupSession
            or SetupSecretSource.ProtectedSetupCookie;

    private static bool ShouldUseSecureSetupCookie(HttpContext ctx) => ctx.Request.IsHttps;

    private static async Task<SetupSecretRequestReadResult> ReadSetupSecretRequestAsync(HttpContext ctx)
    {
        try
        {
            var payload = await ctx.Request.ReadFromJsonAsync<SetupSecretCookieRequest>(
                cancellationToken: ctx.RequestAborted);
            return ValidateRequestSecret(payload?.Secret);
        }
        catch (JsonException)
        {
            return SetupSecretRequestReadResult.Invalid("Setup secret request body must be valid JSON.");
        }
        catch (BadHttpRequestException)
        {
            return SetupSecretRequestReadResult.Invalid("Setup secret request body could not be read.");
        }
        catch (NotSupportedException)
        {
            return SetupSecretRequestReadResult.Invalid("Setup secret request body must be JSON.");
        }
    }

    private static SetupSecretRequestReadResult ValidateRequestSecret(string? rawSecret)
    {
        var secret = rawSecret?.Trim();
        if (string.IsNullOrWhiteSpace(secret))
        {
            return SetupSecretRequestReadResult.Valid(null);
        }

        if (secret.Length > MaxSetupSecretLength)
        {
            return SetupSecretRequestReadResult.Invalid(
                $"Setup secret must be {MaxSetupSecretLength} characters or fewer.");
        }

        if (secret.Any(char.IsControl))
        {
            return SetupSecretRequestReadResult.Invalid("Setup secret contains invalid characters.");
        }

        return SetupSecretRequestReadResult.Valid(secret);
    }

    private static async Task WriteProblemAsync(
        HttpContext ctx,
        int statusCode,
        string title,
        string detail)
    {
        ctx.Response.StatusCode = statusCode;
        await ctx.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        }, ctx.RequestAborted);
    }

    private static async Task<SetupSecretValidationResult> ValidateSetupSecretAsync(
        HttpContext ctx,
        string secret,
        CancellationToken cancellationToken)
    {
        var apiClient = ctx.RequestServices.GetRequiredService<IInstanceOnboardingClient>();
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SetupSecretGateway");

        try
        {
            var body = await apiClient.ValidateInstanceSetupSecretAsync(
                new ValidateSetupSecretRequest { Secret = secret },
                cancellationToken: cancellationToken);

            if (body.Valid)
            {
                return new SetupSecretValidationResult(true, StatusCodes.Status200OK, string.Empty);
            }

            return new SetupSecretValidationResult(
                false, StatusCodes.Status400BadRequest,
                InvalidSetupSecretDetail);
        }
        catch (ApiException ex)
        {
            return ex.StatusCode switch
            {
                StatusCodes.Status410Gone => new(false, StatusCodes.Status410Gone, SetupAlreadyCompletedDetail),
                StatusCodes.Status429TooManyRequests => new(false, StatusCodes.Status429TooManyRequests, TooManyAttemptsDetail),
                StatusCodes.Status403Forbidden => new(false, StatusCodes.Status403Forbidden, InvalidSetupSecretDetail),
                >= 500 => new(false, StatusCodes.Status502BadGateway, ValidationUnavailableDetail),
                _ => new(false, ex.StatusCode, ResolveSafeValidationFailureDetail((HttpStatusCode)ex.StatusCode))
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                "Setup secret validation request failed. ExceptionType: {ExceptionType}",
                ex.GetType().Name);
            return new SetupSecretValidationResult(
                false, StatusCodes.Status503ServiceUnavailable,
                ValidationUnavailableDetail);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Setup secret validation request timed out. ExceptionType: {ExceptionType}",
                ex.GetType().Name);
            return new SetupSecretValidationResult(
                false, StatusCodes.Status503ServiceUnavailable,
                ValidationUnavailableDetail);
        }
    }

    private static string ResolveSafeValidationFailureDetail(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.BadRequest
            ? InvalidSetupSecretDetail
            : ValidationFailedDetail;
    }

    private static void PersistSetupSecret(
        HttpContext ctx,
        ISetupSecretSessionService sessionService,
        string secret,
        bool secureCookie,
        string? userId = null)
    {
        var cookieProtector = ctx.RequestServices.GetRequiredService<ISetupSecretCookieProtector>();
        ctx.Response.Cookies.Append(SetupSecretCookieName, cookieProtector.Protect(secret), CreateSetupCookieOptions(secureCookie));

        var currentAnonymousSessionId = ctx.Request.Cookies[SetupSecretSessionCookieName];
        if (!string.IsNullOrWhiteSpace(currentAnonymousSessionId))
        {
            sessionService.ClearAnonymousSession(currentAnonymousSessionId.Trim());
        }

        var anonymousSessionId = sessionService.CreateAnonymousSession(secret);
        if (!string.IsNullOrWhiteSpace(anonymousSessionId))
        {
            ctx.Response.Cookies.Append(
                SetupSecretSessionCookieName,
                anonymousSessionId,
                CreateSetupCookieOptions(secureCookie));
        }

        var resolvedUserId = string.IsNullOrWhiteSpace(userId) ? ResolveUserId(ctx) : userId;
        if (!string.IsNullOrWhiteSpace(resolvedUserId))
        {
            SetPersistedSecretForUser(sessionService, resolvedUserId, secret);
        }
    }

    private static void ClearSetupSecret(
        HttpContext ctx,
        ISetupSecretSessionService sessionService,
        bool secureCookie,
        string? userId = null)
    {
        var anonymousSessionId = ctx.Request.Cookies[SetupSecretSessionCookieName];
        if (!string.IsNullOrWhiteSpace(anonymousSessionId))
        {
            sessionService.ClearAnonymousSession(anonymousSessionId.Trim());
        }

        ctx.Response.Cookies.Delete(SetupSecretCookieName, CreateSetupCookieOptions(secureCookie));
        ctx.Response.Cookies.Delete(SetupSecretSessionCookieName, CreateSetupCookieOptions(secureCookie));

        var resolvedUserId = string.IsNullOrWhiteSpace(userId) ? ResolveUserId(ctx) : userId;
        if (!string.IsNullOrWhiteSpace(resolvedUserId))
        {
            ClearPersistedSecretForUser(sessionService, resolvedUserId);
        }
    }

    private static string? GetPersistedSecretForUser(ISetupSecretSessionService sessionService, string userId)
    {
        return typeof(ISetupSecretSessionService)
            .GetMethod(nameof(ISetupSecretSessionService.GetForUser), [typeof(string)])?
            .Invoke(sessionService, [userId]) as string;
    }

    private static void SetPersistedSecretForUser(ISetupSecretSessionService sessionService, string userId, string secret)
    {
        typeof(ISetupSecretSessionService)
            .GetMethod(nameof(ISetupSecretSessionService.SetForUser), [typeof(string), typeof(string)])?
            .Invoke(sessionService, [userId, secret]);
    }

    private static void ClearPersistedSecretForUser(ISetupSecretSessionService sessionService, string userId)
    {
        typeof(ISetupSecretSessionService)
            .GetMethod(nameof(ISetupSecretSessionService.ClearForUser), [typeof(string)])?
            .Invoke(sessionService, [userId]);
    }

    private static CookieOptions CreateSetupCookieOptions(bool secureCookie) => new()
    {
        MaxAge = SetupSessionLifetime,
        Path = "/",
        SameSite = SameSiteMode.Lax,
        HttpOnly = true,
        Secure = secureCookie
    };

    // ──────────────────────────────────────────────
    // Internal DTOs
    // ──────────────────────────────────────────────

    internal sealed class SetupSecretCookieRequest
    {
        public string? Secret { get; set; }
    }

    internal sealed class SetupSecretStatusResponse
    {
        public bool HasPersistedSecret { get; set; }
        public bool IsValid { get; set; }
        public string? Error { get; set; }
    }

    internal sealed record SetupSecretRequestReadResult(bool IsValid, string? Secret, string Error)
    {
        public static SetupSecretRequestReadResult Valid(string? secret) => new(true, secret, string.Empty);

        public static SetupSecretRequestReadResult Invalid(string error) => new(false, null, error);
    }

    internal sealed record SetupSecretValidationResult(bool IsValid, int StatusCode, string Error);
}
