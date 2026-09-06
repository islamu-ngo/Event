// ABOUTME: Centralizes middleware pipeline configuration and graceful shutdown for the Blazor BFF server.
// ABOUTME: Extracts XSRF token distribution, startup redirect, and access token capture.

using System.Security.Cryptography;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Middleware;
using Explore.Blazor.Services;
using Explore.ServiceDefaults;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.Extensions;

public static class MiddlewareExtensions
{
    internal const string ContentSecurityPolicyNonceItemKey = "Explore.Blazor.CspNonce";
    internal const string PreserveExplicitSecurityHeadersItemKey = "Explore.Blazor.PreserveExplicitSecurityHeaders";

    private const string ContentSecurityPolicyPrefix =
        "default-src 'self'; " +
        "img-src 'self' data: https: blob:; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "script-src 'self' 'wasm-unsafe-eval' 'unsafe-hashes' 'sha256-qnHnQs7NjQNHHNYv/I9cW+I62HzDJjbnyS/OFzqlix0=' ";

    private const string ContentSecurityPolicySuffix =
        "; " +
        "connect-src 'self' https: http: ws: wss:; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "form-action 'self'";

    private const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(self), payment=()";

    /// <summary>
    /// Adds browser security headers at the BFF boundary before any response is written.
    /// </summary>
    public static WebApplication UseBffSecurityHeaders(this WebApplication app)
    {
        app.Use(AddBffSecurityHeadersAsync);
        return app;
    }

    /// <summary>
    /// Distributes XSRF tokens via cookie on non-static GET requests for the BFF antiforgery pattern.
    /// </summary>
    public static WebApplication UseAntiforgeryTokenMiddleware(this WebApplication app)
    {
        var antiforgery = app.Services.GetRequiredService<IAntiforgery>();
        var secureCookie = !app.Environment.IsDevelopment();
        app.Use((ctx, next) => DistributeAntiforgeryTokenAsync(ctx, next, antiforgery, secureCookie));

        return app;
    }

    public static IApplicationBuilder UseAntiforgeryTokenMiddleware(
        this IApplicationBuilder app,
        WebApplication host)
    {
        var antiforgery = host.Services.GetRequiredService<IAntiforgery>();
        var secureCookie = !host.Environment.IsDevelopment();
        app.Use((ctx, next) => DistributeAntiforgeryTokenAsync(ctx, next, antiforgery, secureCookie));
        return app;
    }

    /// <summary>
    /// Redirects "/" to "/setup" when onboarding is incomplete, and vice versa.
    /// Also gates authentication entry points (/login, /auth/login, /auth/challenge) so that
    /// pre-onboarding users cannot be challenged by an identity provider whose config is not yet
    /// persisted. Excludes /signin-oidc (OIDC callback), /api/InstanceOnboarding/*, /bff/setup-secret*,
    /// and /bff/auth/refresh-* so the setup flow and token refresh paths remain functional.
    /// Resolves the startup gate before endpoint routing to avoid ambiguous "/" matches.
    /// </summary>
    public static WebApplication UseStartupRedirectMiddleware(this WebApplication app)
    {
        var logger = app.Logger;
        app.Use((ctx, next) => HandleStartupRedirectAsync(ctx, next, logger));

        return app;
    }

    public static IApplicationBuilder UseStartupRedirectMiddleware(
        this IApplicationBuilder app,
        WebApplication host)
    {
        app.Use((ctx, next) => HandleStartupRedirectAsync(ctx, next, host.Logger));
        return app;
    }

    public static WebApplication UsePathTenantResolverMiddleware(this WebApplication app)
    {
        app.UseMiddleware<PathTenantResolverMiddleware>();
        return app;
    }

    public static IApplicationBuilder UsePathTenantResolverMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<PathTenantResolverMiddleware>();
        return app;
    }

    /// <summary>
    /// Captures the access token during the HTTP request pipeline for use in Blazor circuits.
    /// Avoids .GetAwaiter().GetResult() anti-pattern in synchronous component code.
    /// </summary>
    public static WebApplication UseAccessTokenCaptureMiddleware(this WebApplication app)
    {
        app.Use(CaptureAccessTokenAsync);
        return app;
    }

    public static IApplicationBuilder UseAccessTokenCaptureMiddleware(this IApplicationBuilder app)
    {
        app.Use(CaptureAccessTokenAsync);
        return app;
    }

    /// <summary>
    /// Logs unauthenticated requests to protected BFF API endpoints for diagnostics.
    /// </summary>
    public static WebApplication UseBffDiagnosticsMiddleware(this WebApplication app)
    {
        var logger = app.Logger;
        app.Use((ctx, next) => LogUnauthenticatedBffRequestsAsync(ctx, next, logger));

        return app;
    }

    public static IApplicationBuilder UseBffDiagnosticsMiddleware(
        this IApplicationBuilder app,
        WebApplication host)
    {
        app.Use((ctx, next) => LogUnauthenticatedBffRequestsAsync(ctx, next, host.Logger));
        return app;
    }

    /// <summary>
    /// Server-side authentication gate for onboarding entry routes that must require a logged-in user.
    /// Razor [Authorize] is inert in this app (App.razor uses CascadingAuthenticationState without
    /// AuthorizeRouteView), and the Blazouter route guards run client-side only. This middleware is
    /// the authoritative gate that redirects anonymous GETs to /login before any component renders.
    /// </summary>
    public static WebApplication UseOnboardingAuthGateMiddleware(this WebApplication app)
    {
        app.Use(EnforceOnboardingAuthGateAsync);
        return app;
    }

    public static IApplicationBuilder UseOnboardingAuthGateMiddleware(this IApplicationBuilder app)
    {
        app.Use(EnforceOnboardingAuthGateAsync);
        return app;
    }

    /// <summary>
    /// Registers graceful shutdown handlers for zero-downtime deployments.
    /// SIGTERM: 25 second grace period. SIGINT: Immediate shutdown.
    /// </summary>
    public static WebApplication ConfigureGracefulShutdown(
        this WebApplication app,
        GracefulShutdownState shutdownState)
    {
        app.Lifetime.ApplicationStopping.Register(() => OnApplicationStopping(app.Logger, shutdownState));
        app.Services.GetRequiredService<HostProcessSignalSubscriptions>().Register(
            (sender, e) => OnCancelKeyPress(app.Logger, shutdownState, e),
            (sender, e) => OnProcessExit(app.Logger, shutdownState));

        return app;
    }

    // --- Private middleware handlers -------------------------------------------------

    private static async Task AddBffSecurityHeadersAsync(HttpContext context, Func<Task> next)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        context.Items[ContentSecurityPolicyNonceItemKey] = nonce;

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var preserveExplicitHeaders = context.Items.ContainsKey(PreserveExplicitSecurityHeadersItemKey);
            if (!preserveExplicitHeaders || !headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
            {
                headers[HeaderNames.ContentSecurityPolicy] =
                    $"{ContentSecurityPolicyPrefix}'nonce-{nonce}'{ContentSecurityPolicySuffix}";
            }

            if (!preserveExplicitHeaders || !headers.ContainsKey(HeaderNames.XFrameOptions))
            {
                headers[HeaderNames.XFrameOptions] = "DENY";
            }

            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            if (IsSensitiveAdmissionPath(context.Request.Path)
                || context.Request.Path.StartsWithSegments(
                    "/bff/events")
                && (context.Request.Path.Value?.Contains(
                        "/registration-orders/",
                        StringComparison.OrdinalIgnoreCase) == true
                    && context.Request.Path.Value?.EndsWith(
                        "/purchase-authority",
                        StringComparison.OrdinalIgnoreCase) == true
                    || context.Request.Path.Value?.Contains(
                        "/participant-readiness/",
                        StringComparison.OrdinalIgnoreCase) == true
                    || context.Request.Path.Value?.Contains(
                        "/admission-tickets/",
                        StringComparison.OrdinalIgnoreCase) == true
                    && context.Request.Path.Value?.Contains(
                        "/transfers",
                        StringComparison.OrdinalIgnoreCase) == true))
            {
                headers[HeaderNames.CacheControl] = "private, no-store";
                headers[HeaderNames.Pragma] = "no-cache";
                headers[HeaderNames.Expires] = "0";
                headers["Referrer-Policy"] = "no-referrer";
            }
            else
            {
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }

            headers["Permissions-Policy"] = PermissionsPolicy;
            return Task.CompletedTask;
        });

        await next();
    }

    private static bool IsSensitiveAdmissionPath(PathString path) =>
        path.StartsWithSegments("/tickets", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/bff/admission-recovery", StringComparison.OrdinalIgnoreCase) ||
        AdmissionCheckInBffTransportExtensions.IsAdmissionCheckInPath(path);

    // Stores the XSRF request token in a non-HttpOnly cookie so the SPA can echo it as a header.
    private static async Task DistributeAntiforgeryTokenAsync(
        HttpContext ctx,
        Func<Task> next,
        IAntiforgery antiforgery,
        bool secureCookie)
    {
        if (HttpMethods.IsGet(ctx.Request.Method) &&
            !System.IO.Path.HasExtension(ctx.Request.Path.Value))
        {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            if (!string.IsNullOrEmpty(tokens.RequestToken))
            {
                ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken, new CookieOptions
                {
                    HttpOnly = false,
                    Secure = secureCookie,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Path = "/"
                });
            }
        }

        await next();
    }

    private static readonly string[] AuthEntryPaths =
    [
        "/login",
        "/auth/login",
        "/auth/challenge"
    ];

    private static async Task HandleStartupRedirectAsync(HttpContext ctx, Func<Task> next, ILogger logger)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method))
        {
            await next();
            return;
        }

        var pathValue = ctx.Request.Path.Value ?? string.Empty;
        var isRoot = string.Equals(pathValue, "/", StringComparison.Ordinal);
        var isSetup = string.Equals(pathValue, "/setup", StringComparison.Ordinal);
        var isAuthEntry = IsAuthEntryPath(pathValue);

        if (!isRoot && !isSetup && !isAuthEntry)
        {
            await next();
            return;
        }

        var status = await ResolveOnboardingStatusAsync(ctx, logger);
        switch (status.Disposition)
        {
            case BffOnboardingDisposition.InteractivePending:
                if (isRoot || isAuthEntry && !HasTrustedSetupSecret(ctx))
                {
                    ctx.Response.Redirect("/setup");
                    return;
                }

                await next();
                return;

            case BffOnboardingDisposition.ConfiguredAdministratorPending:
                if (isSetup)
                {
                    ctx.Response.Redirect("/");
                    return;
                }

                if (isAuthEntry && !IsConfiguredProviderChallenge(ctx, status))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                await next();
                return;

            case BffOnboardingDisposition.Completed:
                if (isSetup)
                {
                    ctx.Response.Redirect("/");
                    return;
                }

                await next();
                return;

            case BffOnboardingDisposition.Closed:
            default:
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
        }
    }

    private static bool IsAuthEntryPath(string path)
    {
        foreach (var entry in AuthEntryPaths)
        {
            if (path.StartsWith(entry, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTrustedSetupSecret(HttpContext ctx)
    {
        var protectedSetupSecret = ctx.Request.Cookies["setup-secret"];
        var cookieProtector = ctx.RequestServices.GetService<ISetupSecretCookieProtector>();
        return cookieProtector?.TryUnprotect(protectedSetupSecret, out var setupSecret) == true
            && !string.IsNullOrWhiteSpace(setupSecret);
    }

    private static bool IsConfiguredProviderChallenge(HttpContext ctx, BffOnboardingStatus status)
    {
        if (!string.Equals(ctx.Request.Path.Value, "/auth/challenge", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providers = ctx.Request.Query["provider"];
        return providers.Count == 1
            && providers[0] is { Length: > 0 } provider
            && status.AllowsProvider(provider);
    }

    private static async Task<BffOnboardingStatus> ResolveOnboardingStatusAsync(
        HttpContext ctx,
        ILogger logger)
    {
        try
        {
            var provider = ctx.RequestServices.GetRequiredService<IBffOnboardingStatusProvider>();
            return await provider.GetStatusAsync(ctx.RequestAborted);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to resolve instance onboarding status for startup redirect.");
            return BffOnboardingStatus.Unknown;
        }
    }

    // Surfaces the OIDC access token into HttpContext.Items and the per-circuit token service.
    private static async Task CaptureAccessTokenAsync(HttpContext ctx, Func<Task> next)
    {
        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            var accessToken = await ctx.GetTokenAsync("access_token");

            if (!string.IsNullOrEmpty(accessToken))
            {
                ctx.Items["AccessToken"] = accessToken;

                var tokenService = ctx.RequestServices.GetService<ICircuitAccessTokenService>();
                tokenService?.SetToken(accessToken);
            }
        }

        await next();
    }

    private static readonly string[] OnboardingProtectedPaths =
    [
        "/onboarding/instance",
        "/onboarding/tenant"
    ];

    private static async Task EnforceOnboardingAuthGateAsync(HttpContext ctx, Func<Task> next)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method))
        {
            await next();
            return;
        }

        var path = ctx.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            await next();
            return;
        }

        var isProtected = false;
        foreach (var protectedPath in OnboardingProtectedPaths)
        {
            if (path.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                isProtected = true;
                break;
            }
        }

        if (!isProtected)
        {
            await next();
            return;
        }

        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            await next();
            return;
        }

        var returnUrl = Uri.EscapeDataString(path + ctx.Request.QueryString);
        ctx.Response.Redirect($"/login?returnUrl={returnUrl}");
    }

    private static async Task LogUnauthenticatedBffRequestsAsync(HttpContext ctx, Func<Task> next, ILogger logger)
    {
        if (ctx.Request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = ctx.GetEndpoint();
            var requiresAuth = endpoint?.Metadata
                .GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>() != null;

            if (requiresAuth && ctx.User?.Identity?.IsAuthenticated != true)
            {
                logger.LogInformation(
                    "BFF: unauthenticated request to protected endpoint {Method} {Path}",
                    ctx.Request.Method, ctx.Request.Path);
            }
        }

        await next();
    }

    // --- Graceful shutdown handlers --------------------------------------------------

    private static void OnApplicationStopping(ILogger logger, GracefulShutdownState shutdownState)
    {
        shutdownState.IsShuttingDown = true;
        logger.LogInformation(
            "SIGTERM received. Starting graceful shutdown. Health checks return 503. " +
            "Accepting requests for {Seconds} more seconds...",
            GracefulShutdownState.GracePeriodSeconds);
    }

    private static void OnCancelKeyPress(
        ILogger logger,
        GracefulShutdownState shutdownState,
        ConsoleCancelEventArgs e)
    {
        logger.LogWarning("SIGINT received. Initiating immediate shutdown...");
        e.Cancel = false;
        shutdownState.CancellationTokenSource.Cancel();
        Environment.Exit(0);
    }

    private static void OnProcessExit(ILogger logger, GracefulShutdownState shutdownState)
    {
        if (!shutdownState.IsShuttingDown)
        {
            shutdownState.IsShuttingDown = true;
            logger.LogInformation(
                "Process exit signal received. Graceful shutdown with {Seconds} second grace period...",
                GracefulShutdownState.GracePeriodSeconds);

            Thread.Sleep(TimeSpan.FromSeconds(GracefulShutdownState.GracePeriodSeconds));
        }
    }
}

/// <summary>
/// Encapsulates mutable state needed by graceful shutdown handlers and health checks.
/// </summary>
public sealed class GracefulShutdownState
{
    public const int GracePeriodSeconds = 25;
    public bool IsShuttingDown { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; } = new();
}
