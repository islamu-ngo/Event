// ABOUTME: Auth-related BFF endpoints: challenge, login, signout, status, providers, debug, refresh-schemes.
// ABOUTME: Includes multi-provider resolution, provider readiness checks, and OIDC metadata validation.

using System.Diagnostics;
using System.Security.Claims;
using Event.Web.BffHosting.Authentication;
using Explore.Atproto.Transport;
using Explore.Blazor.Authentication;
using Explore.Blazor.Constants;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Models;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.Extensions;

public static class BffAuthEndpoints
{
    /// <summary>
    /// Maps authentication endpoints: /auth/challenge, /auth/login, /auth/signout, /auth/status.
    /// Also maps /auth/providers and /auth/debug (dev mode only).
    /// </summary>
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/challenge", HandleChallengeAsync);

        app.MapPost(
                "/auth/atproto/challenge",
                (Func<HttpContext, Task<IResult>>)HandleAtprotoChallengeAsync)
            .ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.AtprotoAuthenticationPolicy)
            .ExcludeFromDescription();

        var atprotoOptions = app.Services
            .GetRequiredService<IOptions<AtprotoAuthenticationOptions>>()
            .Value;
        var atprotoPolicy = new AtprotoOutboundPolicy(
            app.Environment.IsDevelopment() && atprotoOptions.AllowDevelopmentLoopback);
        if (AtprotoClientIdentityFactory.TryCreate(
                atprotoOptions.PublicUrl,
                atprotoOptions.CallbackPath,
                atprotoPolicy,
                out var atprotoIdentity))
        {
            app.MapGet(new Uri(atprotoIdentity.CallbackUri).AbsolutePath, HandleAtprotoCallbackAsync)
                .RequireRateLimiting(RateLimitingExtensions.AtprotoAuthenticationPolicy)
                .ExcludeFromDescription();
        }

        app.MapGet("/auth/atproto/handoff", HandleAtprotoHandoffAsync)
            .ExcludeFromDescription();

        app.MapGet("/auth/login", HandleLoginRedirect);

        app.MapGet("/auth/signout", HandleSignoutAsync);

        app.MapGet("/auth/status", HandleAuthStatus);

        app.MapGet("/auth/providers", HandleGetProviders);

        app.MapPost("/bff/auth/local/login", HandleLocalLoginAsync)
            .ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.LocalAuthenticationPolicy)
            .ExcludeFromDescription();

        app.MapPost("/bff/auth/local/register", HandleLocalRegistrationAsync)
            .ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.LocalAuthenticationPolicy)
            .ExcludeFromDescription();

        app.MapPost("/bff/auth/refresh-schemes", HandleRefreshSchemesAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost("/bff/auth/refresh-session", HandleRefreshSessionAsync)
            .ValidateAntiforgery()
            .RequireAuthorization()
            .ExcludeFromDescription();

        // InteractiveServer self-calls cannot reliably satisfy browser antiforgery semantics,
        // so the server-side onboarding flow uses this authenticated internal variant instead.
        app.MapPost("/bff/auth/refresh-session/internal", HandleInternalRefreshSessionAsync)
            .RequireAuthorization()
            .ExcludeFromDescription();

        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/auth/debug", HandleAuthDebugAsync).RequireAuthorization();
        }

        return app;
    }

    // ──────────────────────────────────────────────
    // Auth endpoint handlers
    // ──────────────────────────────────────────────

    private static async Task HandleChallengeAsync(HttpContext ctx)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AuthEndpoints");
        var returnUrlService = ctx.RequestServices.GetRequiredService<IBffReturnUrlService>();
        var returnUrl = returnUrlService.GetSafeReturnUrl(ctx, logger);
        var provider = ctx.Request.Query["provider"].ToString();

        logger.LogInformation("[AuthEndpoints] Authentication challenge requested for {Provider}", provider);

        var onboardingAdmission = await ResolveOnboardingAdmissionAsync(ctx, provider, isChallengeEndpoint: true);
        if (!ApplyOnboardingAdmission(ctx, onboardingAdmission, logger, "/auth/challenge"))
        {
            return;
        }

        // Resolve which auth scheme to challenge
        var schemeManager = ctx.RequestServices.GetRequiredService<IDynamicAuthSchemeManager>();
        string activePrimaryProvider = schemeManager.GetActivePrimaryProvider();
        if (onboardingAdmission == AuthOnboardingAdmission.AllowCompleted
            && string.Equals(
                activePrimaryProvider,
                "atproto",
                StringComparison.Ordinal)
            && !string.Equals(
                provider,
                "atproto",
                StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect(
                returnUrlService.BuildLoginRedirectUrl(
                    returnUrl,
                    "atproto"));
            return;
        }
        if (onboardingAdmission == AuthOnboardingAdmission.AllowCompleted
            && string.Equals(provider, "keycloak", StringComparison.OrdinalIgnoreCase)
            && activePrimaryProvider != "keycloak")
        {
            ctx.Response.Redirect(returnUrlService.BuildLoginRedirectUrl(returnUrl));
            return;
        }

        var providerReadiness = ctx.RequestServices.GetRequiredService<IBffProviderReadinessService>();
        var schemeName = providerReadiness.ResolveProviderScheme(provider);
        if (string.Equals(schemeName, AuthSchemeNames.Atproto, StringComparison.Ordinal))
        {
            ctx.Response.Redirect(returnUrlService.BuildLoginRedirectUrl(returnUrl, "atproto"));
            return;
        }
        if (!string.IsNullOrEmpty(schemeName))
        {
            if (!await providerReadiness.IsProviderReadyAsync(schemeName, ctx.RequestAborted))
            {
                ctx.Response.Redirect(returnUrlService.BuildLoginRedirectUrl(returnUrl, provider));
                return;
            }
        }
        else
        {
            // No provider specified — try to find a single registered provider
            var registered = await schemeManager.GetRegisteredProviderSchemesAsync();
            var readySchemes = new List<string>();

            foreach (var registeredScheme in registered)
            {
                if (activePrimaryProvider == "atproto"
                    && registeredScheme != AuthSchemeNames.Atproto)
                {
                    continue;
                }

                if (registeredScheme == AuthSchemeNames.Keycloak
                    && activePrimaryProvider != "keycloak")
                {
                    continue;
                }

                if (await providerReadiness.IsProviderReadyAsync(registeredScheme, ctx.RequestAborted))
                {
                    readySchemes.Add(registeredScheme);
                }
            }

            if (readySchemes.Count == 1)
            {
                schemeName = readySchemes[0];
                provider = providerReadiness.MapSchemeToProviderQueryValue(schemeName) ?? provider;
            }
            else
            {
                // Multiple or no providers — redirect to login page for selection
                ctx.Response.Redirect(returnUrlService.BuildLoginRedirectUrl(returnUrl));
                return;
            }
        }

        try
        {
            await ctx.ChallengeAsync(
                schemeName,
                new AuthenticationProperties { RedirectUri = returnUrl });
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var diagnostics = ctx.RequestServices.GetRequiredService<ISafeAuthDiagnosticsPolicy>();
            var diagnostic = diagnostics.CreateDiagnostic("auth_challenge_failed", ex);

            logger.LogError(
                "[AuthEndpoints] Error during {Provider} login challenge " +
                "(errorCode={ErrorCode}, correlationId={CorrelationId}, failureCategory={FailureCategory})",
                schemeName,
                diagnostic.ErrorCode,
                diagnostic.CorrelationId,
                diagnostic.FailureCategory);

            var redirectUrl = diagnostics.BuildLoginRedirectUrl(returnUrl, provider, diagnostic);
            ctx.Response.Redirect(redirectUrl);
        }
    }

    private static async Task<IResult> HandleAtprotoChallengeAsync(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (ctx.Request.ContentLength is > 2048)
        {
            return Results.BadRequest();
        }

        var onboardingAdmission = await ResolveOnboardingAdmissionAsync(
            ctx,
            "Atproto",
            isChallengeEndpoint: true);
        if (onboardingAdmission is not AuthOnboardingAdmission.AllowCompleted
            and not AuthOnboardingAdmission.AllowDuringSetup)
        {
            return onboardingAdmission == AuthOnboardingAdmission.Unavailable
                ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        AtprotoChallengeRequest? request;
        try
        {
            request = await ctx.Request.ReadFromJsonAsync<AtprotoChallengeRequest>(ctx.RequestAborted);
        }
        catch
        {
            return Results.BadRequest();
        }

        if (request is null)
        {
            return Results.BadRequest();
        }
        if (request.CanonicalActorId.HasValue != request.ExpectedCanonicalActorConcurrencyStamp.HasValue
            || request.CanonicalActorId == Guid.Empty
            || request.ExpectedCanonicalActorConcurrencyStamp == Guid.Empty)
        {
            return Results.BadRequest();
        }

        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AuthEndpoints");
        var metrics = ctx.RequestServices.GetRequiredService<AtprotoAuthenticationMetrics>();
        var started = Stopwatch.GetTimestamp();
        var returnPath = GetSafePostedReturnPath(request.ReturnPath);

        try
        {
            var readiness = ctx.RequestServices.GetRequiredService<IBffProviderReadinessService>();
            if (!await readiness.IsProviderReadyAsync(AuthSchemeNames.Atproto, ctx.RequestAborted))
            {
                metrics.Record(
                    AtprotoAuthenticationOperation.Challenge,
                    AtprotoAuthenticationOutcome.ProviderUnavailable,
                    Stopwatch.GetElapsedTime(started));
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "ATProto sign-in is unavailable.");
            }

            var provider = ctx.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>();
            var handler = await provider.GetHandlerAsync(ctx, AuthSchemeNames.Atproto) as AtprotoAuthenticationHandler
                ?? throw new InvalidOperationException("ATProto authentication handler is unavailable.");
            var authorizationUrl = await handler.CreateAuthorizationUrlAsync(
                request.Handle,
                returnPath,
                request.Classification,
                request.CanonicalActorId,
                request.ExpectedCanonicalActorConcurrencyStamp,
                ctx.RequestAborted);
            metrics.Record(
                AtprotoAuthenticationOperation.Challenge,
                AtprotoAuthenticationOutcome.Success,
                Stopwatch.GetElapsedTime(started));
            return Results.Ok(new AtprotoChallengeResponse(authorizationUrl));
        }
        catch (AtprotoProofExpiryException exception)
        {
            metrics.Record(AtprotoAuthenticationOperation.Challenge, AtprotoAuthenticationOutcome.ValidationFailed,
                Stopwatch.GetElapsedTime(started));
            ctx.Response.Headers.RetryAfter = exception.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                title: "Wait for the browser proof to expire before starting another sign-in.");
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            metrics.Record(
                AtprotoAuthenticationOperation.Challenge,
                AtprotoAuthenticationOutcome.Cancelled,
                Stopwatch.GetElapsedTime(started));
            throw;
        }
        catch (Exception exception)
        {
            metrics.Record(
                AtprotoAuthenticationOperation.Challenge,
                AtprotoAuthenticationOutcome.ValidationFailed,
                Stopwatch.GetElapsedTime(started));
            var diagnostics = ctx.RequestServices.GetRequiredService<ISafeAuthDiagnosticsPolicy>();
            var diagnostic = diagnostics.CreateDiagnostic("atproto_challenge_failed", exception);
            logger.LogWarning(
                "ATProto challenge failed (errorCode={ErrorCode}, correlationId={CorrelationId}, failureCategory={FailureCategory})",
                diagnostic.ErrorCode,
                diagnostic.CorrelationId,
                diagnostic.FailureCategory);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "ATProto sign-in could not be started.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = diagnostic.ErrorCode,
                    ["correlationId"] = diagnostic.CorrelationId
                });
        }
    }

    private static async Task HandleAtprotoCallbackAsync(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AuthEndpoints");
        var metrics = ctx.RequestServices.GetRequiredService<AtprotoAuthenticationMetrics>();
        var started = Stopwatch.GetTimestamp();
        try
        {
            var provider = ctx.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>();
            var handler = await provider.GetHandlerAsync(ctx, AuthSchemeNames.Atproto) as AtprotoAuthenticationHandler
                ?? throw new InvalidOperationException("ATProto authentication handler is unavailable.");
            var completion = await handler.CompleteCallbackAsync(ctx.RequestAborted);
            var canonicalOrigin = ctx.RequestServices.GetRequiredService<AtprotoTenantOriginResolver>()
                .ParseCanonicalOrigin();
            if (AtprotoTenantOriginResolver.OriginsEqual(completion.Seed.Origin, canonicalOrigin))
            {
                if (!await SignInAtprotoAsync(ctx, completion.Seed, completion.Session))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                metrics.Record(
                    AtprotoAuthenticationOperation.Callback,
                    AtprotoAuthenticationOutcome.Success,
                    Stopwatch.GetElapsedTime(started));
                ctx.Response.Redirect(completion.Seed.ReturnPath);
                return;
            }

            var handoffCode = await ctx.RequestServices.GetRequiredService<AtprotoTenantSessionHandoffStore>()
                .CreateAsync(completion.Seed, completion.Session, ctx.RequestAborted);
            var destination = new Uri(
                completion.Seed.Origin,
                $"auth/atproto/handoff?code={Uri.EscapeDataString(handoffCode)}");
            metrics.Record(
                AtprotoAuthenticationOperation.Callback,
                AtprotoAuthenticationOutcome.Success,
                Stopwatch.GetElapsedTime(started));
            ctx.Response.Redirect(destination.AbsoluteUri);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            metrics.Record(
                AtprotoAuthenticationOperation.Callback,
                AtprotoAuthenticationOutcome.Cancelled,
                Stopwatch.GetElapsedTime(started));
            throw;
        }
        catch (Exception exception)
        {
            metrics.Record(
                AtprotoAuthenticationOperation.Callback,
                AtprotoAuthenticationOutcome.ValidationFailed,
                Stopwatch.GetElapsedTime(started));
            var diagnostics = ctx.RequestServices.GetRequiredService<ISafeAuthDiagnosticsPolicy>();
            var diagnostic = diagnostics.CreateDiagnostic("atproto_callback_failed", exception);
            logger.LogWarning(
                "ATProto callback failed (errorCode={ErrorCode}, correlationId={CorrelationId}, failureCategory={FailureCategory})",
                diagnostic.ErrorCode,
                diagnostic.CorrelationId,
                diagnostic.FailureCategory);
            ctx.Response.Redirect(diagnostics.BuildLoginRedirectUrl("/", "atproto", diagnostic));
        }
    }

    private static async Task HandleAtprotoHandoffAsync(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        var code = ctx.Request.Query["code"];
        if (code.Count != 1 || code[0] is not { Length: >= 32 and <= 512 })
        {
            ctx.Response.Redirect("/login?provider=atproto&challengeError=1");
            return;
        }

        var handoff = await ctx.RequestServices.GetRequiredService<AtprotoTenantSessionHandoffStore>()
            .ConsumeAsync(code[0]!, ctx.Request, ctx.RequestAborted);
        if (handoff is null)
        {
            ctx.Response.Redirect("/login?provider=atproto&challengeError=1");
            return;
        }

        if (!await SignInAtprotoAsync(ctx, handoff.Seed, handoff.Session))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        ctx.Response.Redirect(handoff.Seed.ReturnPath);
    }

    private static async Task<bool> SignInAtprotoAsync(
        HttpContext ctx,
        AtprotoOAuthFlowSeed seed,
        AtprotoBffSessionResult session)
    {
        // Recheck after provider exchange: proof may have expired since state consumption.
        if (!ctx.RequestServices.GetRequiredService<AtprotoBrowserProof>().Validate(ctx.Request, seed.BrowserBinding))
            return false;
        var identity = new ClaimsIdentity([
            new Claim("sub", session.UserId.ToString("D")),
            new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString("D")),
            new Claim(ClaimTypes.Name, session.Did),
            new Claim("did", session.Did),
            new Claim("tenant_id", seed.TenantId.ToString("D")),
            new Claim("auth_provider", "atproto"),
            new Claim("sid", Guid.CreateVersion7().ToString("D"))
        ], AuthSchemeNames.Atproto, ClaimTypes.Name, ClaimTypes.Role);
        var properties = new AuthenticationProperties
        {
            AllowRefresh = true,
            IsPersistent = true,
            ExpiresUtc = session.ExpiresAt,
            RedirectUri = seed.ReturnPath
        };
        properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = session.AccessToken },
            new AuthenticationToken { Name = "expires_at", Value = session.ExpiresAt.ToString("O") },
            new AuthenticationToken { Name = "token_type", Value = "Bearer" }
        ]);
        var principal = new ClaimsPrincipal(identity);
        var statusProvider = ctx.RequestServices.GetRequiredService<IBffOnboardingStatusProvider>();
        var initialStatus = await statusProvider.GetStatusAsync(ctx.RequestAborted);
        if (initialStatus.Disposition == BffOnboardingDisposition.Closed
            || initialStatus.Disposition == BffOnboardingDisposition.ConfiguredAdministratorPending
            && !initialStatus.AllowsProvider("Atproto"))
        {
            await RejectAtprotoSignInAsync(ctx, principal, "configured_provider_mismatch");
            return false;
        }

        var hasAdminAuthority = await ctx.RequestServices
            .GetRequiredService<BffAdminClaimsTransformation>()
            .EnrichPrincipalAsync(
                principal,
                properties,
                forceRefresh: true,
                synchronizeUser: true,
                cancellationToken: ctx.RequestAborted);
        var refreshedStatus = await statusProvider.GetStatusAsync(ctx.RequestAborted);
        if (initialStatus.Disposition == BffOnboardingDisposition.ConfiguredAdministratorPending
            && (refreshedStatus.Disposition != BffOnboardingDisposition.Completed || !hasAdminAuthority)
            || refreshedStatus.Disposition == BffOnboardingDisposition.Closed)
        {
            await RejectAtprotoSignInAsync(ctx, principal, "onboarding_authority_rejected");
            return false;
        }

        // Asynchronous authority checks must not extend the browser proof's fixed completion deadline.
        if (!ctx.RequestServices.GetRequiredService<AtprotoBrowserProof>().Validate(ctx.Request, seed.BrowserBinding))
            return false;
        ExploreBffCookieSessionHandler.MarkUserSynchronizationCompleted(properties);
        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
        ctx.RequestServices.GetService<ICircuitAccessTokenService>()?.SetToken(session.AccessToken);
        return true;
    }

    private static async Task RejectAtprotoSignInAsync(
        HttpContext ctx,
        ClaimsPrincipal principal,
        string reason)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AuthEndpoints");
        ctx.RequestServices.GetRequiredService<IBffSessionRefreshService>()
            .ClearCircuitTokenState(ctx, principal, logger, reason);
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static string GetSafePostedReturnPath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 2048
        && value.StartsWith('/')
        && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.StartsWith("/\\", StringComparison.Ordinal)
        && !value.Contains('\r')
        && !value.Contains('\n')
            ? value
            : "/";

    private sealed record AtprotoChallengeRequest(
        string? Handle,
        string? ReturnPath,
        string? Classification,
        Guid? CanonicalActorId,
        Guid? ExpectedCanonicalActorConcurrencyStamp);

    private sealed record AtprotoChallengeResponse(string AuthorizationUrl);

    private static async Task HandleLoginRedirect(HttpContext ctx)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AuthEndpoints");

        var returnUrlService = ctx.RequestServices.GetRequiredService<IBffReturnUrlService>();
        var returnUrl = returnUrlService.GetSafeReturnUrl(ctx, logger);
        var provider = ctx.Request.Query["provider"].ToString();
        var onboardingAdmission = await ResolveOnboardingAdmissionAsync(ctx, provider, isChallengeEndpoint: false);
        if (!ApplyOnboardingAdmission(ctx, onboardingAdmission, logger, "/auth/login"))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            var providerReadiness = ctx.RequestServices.GetRequiredService<IBffProviderReadinessService>();
            provider = await providerReadiness.ResolvePreferredProviderForDirectLoginAsync(ctx.RequestAborted);
        }

        var redirectUrl = returnUrlService.BuildChallengeRedirectUrl(returnUrl, provider);

        ctx.Response.Redirect(redirectUrl);
    }

    private static async Task<AuthOnboardingAdmission> ResolveOnboardingAdmissionAsync(
        HttpContext ctx,
        string provider,
        bool isChallengeEndpoint)
    {
        var statusProvider = ctx.RequestServices.GetRequiredService<IBffOnboardingStatusProvider>();
        var status = await statusProvider.GetStatusAsync(ctx.RequestAborted);
        return status.Disposition switch
        {
            BffOnboardingDisposition.Completed => AuthOnboardingAdmission.AllowCompleted,
            BffOnboardingDisposition.InteractivePending => HasTrustedSetupSecret(ctx)
                ? AuthOnboardingAdmission.AllowDuringSetup
                : AuthOnboardingAdmission.RedirectSetup,
            BffOnboardingDisposition.ConfiguredAdministratorPending =>
                isChallengeEndpoint && status.AllowsProvider(provider)
                    ? AuthOnboardingAdmission.AllowDuringSetup
                    : AuthOnboardingAdmission.Deny,
            BffOnboardingDisposition.Closed => AuthOnboardingAdmission.Unavailable,
            _ => AuthOnboardingAdmission.Unavailable
        };
    }

    private static bool ApplyOnboardingAdmission(
        HttpContext ctx,
        AuthOnboardingAdmission admission,
        ILogger logger,
        string endpoint)
    {
        switch (admission)
        {
            case AuthOnboardingAdmission.AllowCompleted:
            case AuthOnboardingAdmission.AllowDuringSetup:
                return true;
            case AuthOnboardingAdmission.RedirectSetup:
                logger.LogInformation(
                    "[AuthEndpoints] Redirecting {Endpoint} to /setup because interactive onboarding is pending.",
                    endpoint);
                ctx.Response.Redirect("/setup");
                return false;
            case AuthOnboardingAdmission.Deny:
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return false;
            case AuthOnboardingAdmission.Unavailable:
            default:
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return false;
        }
    }

    private static bool HasTrustedSetupSecret(HttpContext ctx)
    {
        var protectedSetupSecret = ctx.Request.Cookies["setup-secret"];
        var cookieProtector = ctx.RequestServices.GetService<ISetupSecretCookieProtector>();
        return cookieProtector?.TryUnprotect(protectedSetupSecret, out var setupSecret) == true
            && !string.IsNullOrWhiteSpace(setupSecret);
    }

    private enum AuthOnboardingAdmission
    {
        AllowCompleted,
        AllowDuringSetup,
        RedirectSetup,
        Deny,
        Unavailable
    }

    private static async Task HandleSignoutAsync(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store, no-cache";
        ctx.Response.Headers.Pragma = "no-cache";

        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AuthEndpoints");
        var returnUrlService = ctx.RequestServices.GetRequiredService<IBffReturnUrlService>();
        var returnUrl = returnUrlService.GetSafeReturnUrl(ctx, logger);

        logger.LogInformation(
            "[AuthEndpoints] /auth/signout hit - Url: {Url} ReturnUrl: {ReturnUrl}",
            ctx.Request.GetDisplayUrl(), returnUrl);

        var cookieAuthResult = AuthenticateResult.NoResult();
        try
        {
            cookieAuthResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[AuthEndpoints] Could not authenticate cookie session during signout");
        }

        try
        {
            // Always clear the local BFF cookie session. Remote provider signout is best-effort,
            // but local cookie clearing is the security boundary and must not be reported as success
            // if it fails.
            ctx.RequestServices.GetRequiredService<IBffSessionRefreshService>()
                .ClearCircuitTokenState(ctx, cookieAuthResult.Principal, logger, "signout");
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[AuthEndpoints] Could not clear cookie session during signout");
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                ctx.Response.ContentType = "application/problem+json";
                await ctx.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                    title = "Logout Failed",
                    status = StatusCodes.Status500InternalServerError,
                    detail = "The local session could not be cleared. Please retry signout."
                });
            }

            return;
        }

        using (var revokeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await ctx.RequestServices.GetRequiredService<IBffSessionRefreshService>()
                .RevokeAtprotoSessionAsync(ctx, cookieAuthResult, revokeTimeout.Token);
        }

        if (cookieAuthResult.Succeeded && cookieAuthResult.Principal?.Identity?.IsAuthenticated == true)
        {
            IReadOnlyCollection<string> registered = [];

            try
            {
                var schemeManager = ctx.RequestServices.GetRequiredService<IDynamicAuthSchemeManager>();
                registered = await schemeManager.GetRegisteredProviderSchemesAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[AuthEndpoints] Could not enumerate remote signout providers");
            }

            foreach (var scheme in registered)
            {
                if (scheme is AuthSchemeNames.Keycloak or AuthSchemeNames.Google)
                {
                    try
                    {
                        await ctx.SignOutAsync(scheme,
                            new AuthenticationProperties { RedirectUri = returnUrl });
                        logger.LogInformation("[AuthEndpoints] Signed out of {Scheme}", scheme);
                        return; // OIDC signout handles redirect
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex,
                            "[AuthEndpoints] Could not sign out of {Scheme} — falling back to local redirect",
                            scheme);
                    }
                }
            }
        }

        if (!ctx.Response.HasStarted)
        {
            ctx.Response.Redirect(returnUrl);
            logger.LogInformation("[AuthEndpoints] Signout completed with local redirect");
        }
    }

    private static async Task<IResult> HandleRefreshSessionAsync(
        HttpContext ctx,
        CancellationToken cancellationToken)
    {
        var refreshService = ctx.RequestServices.GetRequiredService<IBffSessionRefreshService>();
        return await refreshService.RefreshSessionAsync(ctx, cancellationToken);
    }

    private static async Task<IResult> HandleLocalLoginAsync(
        HttpContext ctx,
        LocalBffLoginRequest request,
        ILocalAuthClient client,
        IDynamicAuthSchemeManager schemeManager,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                schemeManager.GetActivePrimaryProvider(),
                "local",
                StringComparison.Ordinal))
        {
            return LocalAuthenticationFailure(
                StatusCodes.Status409Conflict);
        }

        var initialStatus = await ctx.RequestServices
            .GetRequiredService<IBffOnboardingStatusProvider>()
            .GetStatusAsync(cancellationToken);
        if (!AllowsLocalAuthentication(initialStatus))
        {
            return LocalAuthenticationFailure(StatusCodes.Status409Conflict);
        }

        try
        {
            LocalAuthResponseDto response = await client.LoginLocalIdentityAsync(
                new LocalAuthRequestDto
                {
                    Email = request.Email,
                    Password = request.Password
                },
                cancellationToken: cancellationToken);
            return await CompleteLocalSignInAsync(
                ctx,
                response,
                request.IsPersistent,
                request.ReturnUrl,
                initialStatus);
        }
        catch (ApiException exception)
        {
            return LocalAuthenticationFailure(exception.StatusCode);
        }
    }

    private static async Task<IResult> HandleLocalRegistrationAsync(
        HttpContext ctx,
        LocalBffRegistrationRequest request,
        ILocalAuthClient client,
        IDynamicAuthSchemeManager schemeManager,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                schemeManager.GetActivePrimaryProvider(),
                "local",
                StringComparison.Ordinal))
        {
            return LocalAuthenticationFailure(
                StatusCodes.Status409Conflict);
        }

        var initialStatus = await ctx.RequestServices
            .GetRequiredService<IBffOnboardingStatusProvider>()
            .GetStatusAsync(cancellationToken);
        if (!AllowsLocalAuthentication(initialStatus))
        {
            return LocalAuthenticationFailure(StatusCodes.Status409Conflict);
        }

        try
        {
            LocalRegistrationResponseDto response =
                await client.RegisterLocalIdentityAsync(
                    new LocalRegistrationRequestDto
                    {
                        Email = request.Email,
                        Password = request.Password,
                        FirstName = request.FirstName,
                        LastName = request.LastName
                    },
                    cancellationToken: cancellationToken);
            if (response.Success != true || response.Authentication is null)
            {
                return LocalAuthenticationFailure(StatusCodes.Status400BadRequest);
            }

            return await CompleteLocalSignInAsync(
                ctx,
                response.Authentication,
                request.IsPersistent,
                request.ReturnUrl,
                initialStatus);
        }
        catch (ApiException exception)
        {
            return LocalAuthenticationFailure(exception.StatusCode);
        }
    }

    private static async Task<IResult> CompleteLocalSignInAsync(
        HttpContext ctx,
        LocalAuthResponseDto response,
        bool isPersistent,
        string? returnUrl,
        BffOnboardingStatus initialStatus)
    {
        if (response.Success != true
            || response.UserId is not { } userId
            || userId == Guid.Empty
            || string.IsNullOrWhiteSpace(response.Token)
            || response.ExpiresAt is not { } expiresAt)
        {
            return LocalAuthenticationFailure(StatusCodes.Status401Unauthorized);
        }

        var claims = new List<Claim>
        {
            new("sub", userId.ToString("D")),
            new(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new("internal_user_id", userId.ToString("D")),
            new(ClaimTypes.Name, response.Email ?? userId.ToString("D")),
            new(ClaimTypes.Email, response.Email ?? string.Empty),
            new("given_name", response.FirstName ?? string.Empty),
            new("family_name", response.LastName ?? string.Empty),
            new("email_verified", response.EmailVerified == true ? "true" : "false"),
            new("auth_provider", "local"),
            new("sid", Guid.CreateVersion7().ToString("D"))
        };
        claims.AddRange((response.Roles ?? [])
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            "LocalIdentity",
            ClaimTypes.Name,
            ClaimTypes.Role));
        string safeReturnUrl = ResolveLocalReturnUrl(returnUrl);
        var properties = new AuthenticationProperties
        {
            AllowRefresh = false,
            IsPersistent = isPersistent,
            ExpiresUtc = expiresAt,
            RedirectUri = safeReturnUrl
        };
        properties.Items[
            EventBffAuthenticationConstants.AuthenticationProviderPropertyKey] = "local";
        properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = response.Token },
            new AuthenticationToken { Name = "expires_at", Value = expiresAt.ToString("O") },
            new AuthenticationToken { Name = "token_type", Value = "Bearer" }
        ]);
        var statusProvider = ctx.RequestServices
            .GetRequiredService<IBffOnboardingStatusProvider>();
        statusProvider.Invalidate();
        bool hasAdminAuthority = await ctx.RequestServices
            .GetRequiredService<BffAdminClaimsTransformation>()
            .EnrichPrincipalAsync(
                principal,
                properties,
                forceRefresh: true,
                synchronizeUser: false,
                cancellationToken: ctx.RequestAborted);
        BffOnboardingStatus refreshedStatus = await statusProvider
            .GetStatusAsync(ctx.RequestAborted);
        if (refreshedStatus.Disposition == BffOnboardingDisposition.Closed
            || initialStatus.Disposition
                == BffOnboardingDisposition.ConfiguredAdministratorPending
            && (refreshedStatus.Disposition != BffOnboardingDisposition.Completed
                || !hasAdminAuthority))
        {
            return LocalAuthenticationFailure(StatusCodes.Status403Forbidden);
        }

        ExploreBffCookieSessionHandler.MarkUserSynchronizationCompleted(properties);
        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
        ctx.RequestServices.GetService<ICircuitAccessTokenService>()?
            .SetToken(response.Token);
        return Results.Ok(new { redirectUrl = safeReturnUrl });
    }

    private static IResult LocalAuthenticationFailure(int statusCode)
    {
        int safeStatus = statusCode is StatusCodes.Status400BadRequest
            or StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden
            or StatusCodes.Status409Conflict
            or StatusCodes.Status429TooManyRequests
            ? statusCode
            : StatusCodes.Status503ServiceUnavailable;
        return Results.Problem(
            title: safeStatus == StatusCodes.Status401Unauthorized
                ? "Authentication failed"
                : "Local authentication unavailable",
            detail: safeStatus == StatusCodes.Status401Unauthorized
                ? "The submitted credentials are invalid."
                : "The local authentication request could not be completed.",
            statusCode: safeStatus);
    }

    private static bool AllowsLocalAuthentication(BffOnboardingStatus status) =>
        status.Disposition != BffOnboardingDisposition.Closed
        && (status.Disposition
                != BffOnboardingDisposition.ConfiguredAdministratorPending
            || status.AllowsProvider("local"));

    private static string ResolveLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    private static async Task<IResult> HandleInternalRefreshSessionAsync(
        HttpContext ctx,
        CancellationToken cancellationToken)
    {
        var selfCallTokenService = ctx.RequestServices.GetRequiredService<IBffSelfCallTokenService>();
        if (!selfCallTokenService.Validate(ctx))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await HandleRefreshSessionAsync(ctx, cancellationToken);
    }

    private static IResult HandleAuthStatus(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store, no-cache";
        ctx.Response.Headers.Pragma = "no-cache";

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            return Results.Ok(new { isAuthenticated = true, name = ctx.User.Identity.Name });
        }

        return Results.Ok(new { isAuthenticated = false });
    }

    private static async Task<IResult> HandleAuthDebugAsync(
        IBffAuthDiagnosticsService diagnosticsService,
        CancellationToken cancellationToken)
    {
        var result = await diagnosticsService.BuildDebugSnapshotAsync(cancellationToken);
        return Results.Ok(result);
    }

    private static async Task HandleGetProviders(HttpContext ctx)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AuthEndpoints");

        try
        {
            var schemeManager = ctx.RequestServices.GetRequiredService<IDynamicAuthSchemeManager>();
            var providerReadiness = ctx.RequestServices.GetRequiredService<IBffProviderReadinessService>();
            var registered = await schemeManager.GetRegisteredProviderSchemesAsync();

            logger.LogInformation(
                "[AuthEndpoints] /auth/providers — registered schemes: [{Schemes}]",
                string.Join(", ", registered));

            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var keycloakFromEnv = !string.IsNullOrEmpty(config["Keycloak:Authority"]);

            var providers = new List<object>();
            string primaryProvider = schemeManager.GetActivePrimaryProvider();
            bool atprotoLoginEnabled = primaryProvider == "atproto";
            if (primaryProvider == "local")
            {
                providers.Add(new
                {
                    name = "local",
                    displayName = "Local Identity",
                    type = "credentials",
                    recommended = true
                });
            }

            foreach (var scheme in registered)
            {
                if (primaryProvider == "atproto"
                    && scheme != AuthSchemeNames.Atproto)
                {
                    continue;
                }

                if (scheme == AuthSchemeNames.Keycloak
                    && primaryProvider != "keycloak")
                {
                    continue;
                }

                var ready = await providerReadiness.IsProviderReadyAsync(scheme, ctx.RequestAborted);
                if (!ready)
                {
                    // Provider is registered but discovery endpoint is unreachable.
                    // For env/config-sourced providers (authority + clientId present),
                    // still include them — the user configured them intentionally.
                    var hasMinimalConfig = providerReadiness.HasMinimalProviderConfig(scheme);
                    if (!hasMinimalConfig)
                    {
                        logger.LogWarning(
                            "[AuthEndpoints] /auth/providers — scheme {Scheme} is registered but NOT ready and has no env config — skipping",
                            scheme);
                        continue;
                    }

                    logger.LogWarning(
                        "[AuthEndpoints] /auth/providers — scheme {Scheme} discovery check failed but env/config present — including anyway",
                        scheme);
                }

                providers.Add(new
                {
                    name = providerReadiness.MapSchemeToProviderQueryValue(scheme) ?? scheme,
                    displayName = scheme switch
                    {
                        AuthSchemeNames.Keycloak => "Keycloak",
                        AuthSchemeNames.Google => "Google",
                        AuthSchemeNames.Atproto => "AT Protocol",
                        _ => scheme
                    },
                    type = scheme switch
                    {
                        AuthSchemeNames.Atproto => "handle_input",
                        _ => "button"
                    },
                    recommended = primaryProvider == "atproto"
                        ? scheme == AuthSchemeNames.Atproto
                        : scheme == AuthSchemeNames.Keycloak
                          && keycloakFromEnv
                });
                atprotoLoginEnabled |= scheme == AuthSchemeNames.Atproto;
            }

            logger.LogInformation(
                "[AuthEndpoints] /auth/providers — returning {Count} ready provider(s)",
                providers.Count);

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new
            {
                primaryProvider,
                atprotoLoginEnabled,
                providers
            });
        }
        catch (Exception ex)
        {
            var diagnostics = ctx.RequestServices.GetRequiredService<ISafeAuthDiagnosticsPolicy>();
            var diagnostic = diagnostics.CreateDiagnostic("auth_provider_resolution_failed", ex);

            logger.LogError(
                "Unhandled exception in HandleGetProviders " +
                "(errorCode={ErrorCode}, correlationId={CorrelationId}, failureCategory={FailureCategory})",
                diagnostic.ErrorCode,
                diagnostic.CorrelationId,
                diagnostic.FailureCategory);

            if (!ctx.Response.HasStarted)
            {
                await Results.Problem(
                    detail: "Authentication providers could not be resolved.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Provider Resolution Failed",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = diagnostic.ErrorCode,
                        ["correlationId"] = diagnostic.CorrelationId
                    }).ExecuteAsync(ctx);
            }
        }
    }

    private static async Task HandleRefreshSchemesAsync(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store, no-cache";
        ctx.Response.Headers.Pragma = "no-cache";

        var setupSecretResolution = ctx.RequestServices.GetRequiredService<ISetupSecretResolver>().Resolve(ctx);
        var setupSecret = setupSecretResolution.Found ? setupSecretResolution.Secret : null;
        var schemeManager = ctx.RequestServices.GetRequiredService<IDynamicAuthSchemeManager>();
        await schemeManager.RefreshSchemesAsync(setupSecret);

        var registered = await schemeManager.GetRegisteredProviderSchemesAsync();
        await ctx.Response.WriteAsJsonAsync(new { refreshed = true, providers = registered });
    }

}
