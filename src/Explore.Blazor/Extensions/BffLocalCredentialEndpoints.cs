// ABOUTME: Hosts the restricted first-use Local credential replacement browser boundary.
// ABOUTME: Applies browser mutation protections without creating an ordinary authenticated session.

using Explore.Blazor.Models;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

namespace Explore.Blazor.Extensions;

public static class BffLocalCredentialEndpoints
{
    public const string HttpClientName = "LocalCredentialReplacement";
    public const string ReplacementPath = "/bff/auth/local/credential-replacement";
    public const string PasswordChangePath = "/auth/local/change-password";

    public static IApplicationBuilder UseLocalCredentialPrivacyHeaders(this IApplicationBuilder app) =>
        app.Use(InvokeLocalCredentialPrivacyHeadersAsync);

    private static async Task InvokeLocalCredentialPrivacyHeadersAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value?.TrimEnd('/');
        if (string.Equals(path, "/bff/auth/local/login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, ReplacementPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, PasswordChangePath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store, private";
                return Task.CompletedTask;
            });
        }

        await next(context);
    }

    public static WebApplication MapLocalCredentialEndpoints(this WebApplication app)
    {
        app.MapPost(ReplacementPath, HandleReplacementAsync)
            .ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.LocalAuthenticationPolicy)
            .ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> HandleReplacementAsync(
        HttpContext context,
        LocalBffCredentialReplacementRequest request,
        LocalCredentialChallengeCookie challengeCookie,
        IHttpClientFactory clientFactory,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!challengeCookie.TryRead(context, out var challenge))
        {
            challengeCookie.Delete(context);
            return Failure(StatusCodes.Status401Unauthorized, "replacement_required");
        }

        if (request.NewPassword is not { Length: >= 12 and <= 128 })
        {
            return Failure(StatusCodes.Status400BadRequest, "password_rejected");
        }

        var admission = await GetFreshAdmissionAsync(context, cancellationToken);
        if (admission != LocalCredentialAdmission.Allowed)
        {
            challengeCookie.Delete(context);
            return admission == LocalCredentialAdmission.Denied
                ? Failure(StatusCodes.Status409Conflict, "replacement_conflict")
                : Failure(StatusCodes.Status503ServiceUnavailable, "replacement_unavailable");
        }

        try
        {
            using var outbound = new HttpRequestMessage(HttpMethod.Post, "/api/auth/local/credential-replacement")
            {
                Content = JsonContent.Create(new LocalCredentialReplacementRequestDto
                {
                    NewPassword = request.NewPassword
                })
            };
            outbound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", challenge);
            using var client = clientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(outbound, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NoContent:
                    challengeCookie.Delete(context);
                    await ClearLocalSessionAsync(context);
                    return Results.Ok(new { redirectUrl = "/login" });
                case HttpStatusCode.BadRequest:
                    return Failure(StatusCodes.Status400BadRequest, "password_rejected");
                case HttpStatusCode.Unauthorized:
                    challengeCookie.Delete(context);
                    return Failure(StatusCodes.Status401Unauthorized, "replacement_required");
                case HttpStatusCode.Conflict:
                    challengeCookie.Delete(context);
                    return Failure(StatusCodes.Status409Conflict, "replacement_conflict");
                case HttpStatusCode.TooManyRequests:
                    return Failure(StatusCodes.Status429TooManyRequests, "rate_limited");
                default:
                    return Failure(StatusCodes.Status503ServiceUnavailable, "replacement_unavailable");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return Failure(StatusCodes.Status503ServiceUnavailable, "replacement_unavailable");
        }
    }

    internal static async Task<LocalCredentialAdmission> GetFreshAdmissionAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var statusProvider = context.RequestServices.GetRequiredService<IBffOnboardingStatusProvider>();
        statusProvider.Invalidate();
        var status = await statusProvider.GetStatusAsync(cancellationToken);
        if (status.Disposition is not (BffOnboardingDisposition.Completed or BffOnboardingDisposition.InteractivePending)
            && !(status.Disposition == BffOnboardingDisposition.ConfiguredAdministratorPending
                && status.AllowsProvider("local")))
        {
            return LocalCredentialAdmission.Denied;
        }

        var configured = context.RequestServices.GetRequiredService<IConfiguration>()["Authentication:Provider"]
            ?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(configured))
        {
            return configured == "local" ? LocalCredentialAdmission.Allowed : LocalCredentialAdmission.Denied;
        }

        try
        {
            var client = context.RequestServices.GetRequiredService<IInstanceOnboardingClient>();
            var configuration = await client.GetInstanceOnboardingAuthProviderConfigurationAsync(
                cancellationToken: cancellationToken);
            return configuration?.PrimaryProviderId == 4
                ? LocalCredentialAdmission.Allowed
                : LocalCredentialAdmission.Denied;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return LocalCredentialAdmission.Unavailable;
        }
    }

    internal static async Task ClearLocalSessionAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("LocalCredentialReplacement");
        context.RequestServices.GetRequiredService<IBffSessionRefreshService>()
            .ClearCircuitTokenState(context, context.User, logger, "local_credential_replacement");
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static IResult Failure(int statusCode, string code) => Results.Problem(
        title: "Password replacement could not be completed",
        statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    internal enum LocalCredentialAdmission
    {
        Allowed = 1,
        Denied = 2,
        Unavailable = 3
    }
}
