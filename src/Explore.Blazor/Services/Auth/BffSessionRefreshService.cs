using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Explore.Blazor.Services.Auth;

public interface IBffSessionRefreshService
{
    Task<IResult> RefreshSessionAsync(HttpContext context, CancellationToken cancellationToken);

    Task RevokeAtprotoSessionAsync(
        HttpContext context,
        AuthenticateResult authentication,
        CancellationToken cancellationToken);

    void ClearCircuitTokenState(HttpContext context, ClaimsPrincipal? principal, ILogger logger, string reason);
}

public sealed class BffSessionRefreshService(
    BffAdminClaimsTransformation adminClaimsTransformation,
    IBffAccessTokenAssessmentService tokenAssessmentService,
    IHttpClientFactory httpClientFactory,
    AtprotoBootstrapAssertionService assertionService,
    AtprotoTenantOriginResolver tenantOriginResolver,
    AtprotoAuthenticationMetrics atprotoMetrics)
    : IBffSessionRefreshService
{
    private const int MaximumPlatformTokenBytes = 16 * 1024;
    private const int MaximumRefreshResponseBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IResult> RefreshSessionAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AuthEndpoints");

        var authResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!authResult.Succeeded || authResult.Principal is null || authResult.Properties is null)
        {
            logger.LogWarning("[AuthEndpoints] Refresh session failed because cookie authentication did not succeed");
            return Results.Unauthorized();
        }

        if (HasSingleClaim(authResult.Principal, "auth_provider", "atproto"))
        {
            return await RefreshAtprotoSessionAsync(context, authResult, logger, cancellationToken)
                .ConfigureAwait(false);
        }

        var accessToken = authResult.Properties.GetTokenValue("access_token");
        var tokenAssessment = tokenAssessmentService.Assess(accessToken);
        if (!tokenAssessment.IsUsable || string.IsNullOrWhiteSpace(accessToken))
        {
            ClearCircuitTokenState(context, authResult.Principal, logger, tokenAssessment.Reason);
            logger.LogWarning(
                "[AuthEndpoints] Refresh session completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} SubjectPresent={SubjectPresent}",
                "rejected", tokenAssessment.Reason, "session_refresh",
                authResult.Principal.TryGetSessionRefreshSubject(out _));
            return Results.Json(
                new { refreshed = false, reason = tokenAssessment.Reason },
                statusCode: StatusCodes.Status409Conflict);
        }

        var onboardingStatusProvider = context.RequestServices
            .GetRequiredService<IBffOnboardingStatusProvider>();
        var initialOnboardingStatus = await onboardingStatusProvider
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        var adminClaimsUpdated = await adminClaimsTransformation.EnrichPrincipalAsync(
            authResult.Principal,
            authResult.Properties,
            forceRefresh: true,
            synchronizeUser: true,
            cancellationToken: cancellationToken);
        var refreshedOnboardingStatus = await onboardingStatusProvider
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!IsOnboardingSessionAllowed(
                authResult.Principal,
                initialOnboardingStatus,
                refreshedOnboardingStatus,
                adminClaimsUpdated))
        {
            return await RequireOnboardingReauthenticationAsync(
                context,
                authResult,
                logger,
                "onboarding_authority_rejected").ConfigureAwait(false);
        }

        var userId = tokenAssessmentService.ResolveUserId(authResult.Principal);
        var tokenStoreResult = context.RequestServices.GetRequiredService<ICircuitTokenStore>()
            .Store(userId ?? string.Empty,
                authResult.Principal.TryGetSessionId(out var sessionId) ? sessionId.PartitionKey : null, accessToken);
        if (!tokenStoreResult.Accepted)
        {
            logger.LogWarning(
                "[AuthEndpoints] Token handoff completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} SubjectPresent={SubjectPresent}",
                "rejected", tokenStoreResult.RejectionCode, "session_refresh", !string.IsNullOrWhiteSpace(userId));
            return Results.Json(
                new { refreshed = false, reason = "token_handoff_failed" },
                statusCode: StatusCodes.Status409Conflict);
        }

        ExploreBffCookieSessionHandler.MarkUserSynchronizationCompleted(authResult.Properties);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            authResult.Principal,
            authResult.Properties);

        logger.LogInformation(
            "[AuthEndpoints] Refresh session completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} AdminClaimsUpdated={AdminClaimsUpdated}",
            "accepted", tokenAssessment.Reason, "session_refresh", adminClaimsUpdated);

        return Results.Ok(new { refreshed = true, adminClaimsUpdated, tokenStatus = tokenAssessment.Reason });
    }

    public async Task RevokeAtprotoSessionAsync(
        HttpContext context,
        AuthenticateResult authentication,
        CancellationToken cancellationToken)
    {
        if (!TryCreateAtprotoRequestContext(context, authentication, out var session))
        {
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = CreateAtprotoRequest(HttpMethod.Delete, session);
            using var response = await httpClientFactory.CreateClient(ApiBackedOAuthSessionStore.HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            atprotoMetrics.Record(
                AtprotoAuthenticationOperation.Revoke,
                response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? AtprotoAuthenticationOutcome.Success
                    : AtprotoAuthenticationOutcome.ProviderUnavailable,
                Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException)
        {
            atprotoMetrics.Record(
                AtprotoAuthenticationOperation.Revoke,
                AtprotoAuthenticationOutcome.Cancelled,
                Stopwatch.GetElapsedTime(started));
        }
        catch (Exception)
        {
            atprotoMetrics.Record(
                AtprotoAuthenticationOperation.Revoke,
                AtprotoAuthenticationOutcome.ProviderUnavailable,
                Stopwatch.GetElapsedTime(started));
        }
    }

    public void ClearCircuitTokenState(HttpContext context, ClaimsPrincipal? principal, ILogger logger, string reason)
    {
        var tokenService = context.RequestServices.GetService<ICircuitAccessTokenService>();
        tokenService?.ClearToken();

        context.RequestServices.GetService<ICircuitUserContext>()?.Clear();
        context.RequestServices.GetService<IBffAuthCookieStore>()?.Clear();

        // ICircuitAccessTokenService.ClearToken() already delegates to ICircuitTokenStore
        // for the scoped user/session. For full user-wide clearing (e.g., signout with
        // unknown session), also clear via the store directly.
        var userId = tokenAssessmentService.ResolveUserId(principal);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var tokenStore = context.RequestServices.GetService<ICircuitTokenStore>();
            var sessionId = principal.TryGetSessionId(out var resolvedSessionId) ? resolvedSessionId.PartitionKey : null;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                tokenStore?.ClearUser(userId);
            }
            else
            {
                tokenStore?.ClearSession(userId, sessionId);
            }
        }

        logger.LogDebug(
            "[AuthEndpoints] Circuit token cleanup completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} SubjectPresent={SubjectPresent} SessionPresent={SessionPresent}",
            "cleared", reason, "session_refresh",
            principal.TryGetSessionRefreshSubject(out _),
            principal.TryGetSessionId(out _));
    }

    private async Task<IResult> RefreshAtprotoSessionAsync(
        HttpContext context,
        AuthenticateResult authentication,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        if (!TryCreateAtprotoRequestContext(context, authentication, out var session))
        {
            return await RequireAtprotoReauthenticationAsync(
                context,
                authentication,
                logger,
                started,
                AtprotoAuthenticationOutcome.ValidationFailed);
        }

        try
        {
            using var request = CreateAtprotoRequest(HttpMethod.Post, session);
            using var response = await httpClientFactory.CreateClient(ApiBackedOAuthSessionStore.HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await RequireAtprotoReauthenticationAsync(
                    context,
                    authentication,
                    logger,
                    started,
                    AtprotoAuthenticationOutcome.ReauthenticationRequired);
            }

            var body = await ReadBoundedAsync(
                response.Content,
                MaximumRefreshResponseBytes,
                cancellationToken).ConfigureAwait(false);
            var refreshed = JsonSerializer.Deserialize<AtprotoRefreshBridgeResponse>(body, JsonOptions);
            if (refreshed is null
                || refreshed.UserId != session.UserId
                || !string.Equals(refreshed.Did, session.Did, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(refreshed.AccessToken)
                || refreshed.AccessToken.Length > MaximumPlatformTokenBytes
                || refreshed.ExpiresAt <= DateTimeOffset.UtcNow
                || refreshed.ExpiresAt > DateTimeOffset.UtcNow.AddHours(1))
            {
                return await RequireAtprotoReauthenticationAsync(
                    context,
                    authentication,
                    logger,
                    started,
                    AtprotoAuthenticationOutcome.TokenInvalid);
            }

            authentication.Properties!.ExpiresUtc = refreshed.ExpiresAt;
            authentication.Properties.StoreTokens([
                new AuthenticationToken { Name = "access_token", Value = refreshed.AccessToken },
                new AuthenticationToken { Name = "expires_at", Value = refreshed.ExpiresAt.ToString("O") },
                new AuthenticationToken { Name = "token_type", Value = "Bearer" }
            ]);
            var onboardingStatusProvider = context.RequestServices
                .GetRequiredService<IBffOnboardingStatusProvider>();
            var initialOnboardingStatus = await onboardingStatusProvider
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            var adminClaimsUpdated = await adminClaimsTransformation.EnrichPrincipalAsync(
                authentication.Principal!,
                authentication.Properties,
                forceRefresh: true,
                synchronizeUser: true,
                cancellationToken: cancellationToken);
            var refreshedOnboardingStatus = await onboardingStatusProvider
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!IsOnboardingSessionAllowed(
                    authentication.Principal!,
                    initialOnboardingStatus,
                    refreshedOnboardingStatus,
                    adminClaimsUpdated))
            {
                return await RequireOnboardingReauthenticationAsync(
                    context,
                    authentication,
                    logger,
                    "onboarding_authority_rejected").ConfigureAwait(false);
            }
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                authentication.Principal!,
                authentication.Properties);
            context.RequestServices.GetService<ICircuitAccessTokenService>()?.SetToken(refreshed.AccessToken);
            atprotoMetrics.Record(
                AtprotoAuthenticationOperation.Refresh,
                AtprotoAuthenticationOutcome.Success,
                Stopwatch.GetElapsedTime(started));
            return Results.Ok(new { refreshed = true, adminClaimsUpdated, tokenStatus = "atproto_refreshed" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            atprotoMetrics.Record(
                AtprotoAuthenticationOperation.Refresh,
                AtprotoAuthenticationOutcome.Cancelled,
                Stopwatch.GetElapsedTime(started));
            throw;
        }
        catch (Exception)
        {
            return await RequireAtprotoReauthenticationAsync(
                context,
                authentication,
                logger,
                started,
                AtprotoAuthenticationOutcome.ProviderUnavailable);
        }
    }

    private async Task<IResult> RequireAtprotoReauthenticationAsync(
        HttpContext context,
        AuthenticateResult authentication,
        ILogger logger,
        long started,
        AtprotoAuthenticationOutcome outcome)
    {
        var result = await RequireOnboardingReauthenticationAsync(
            context,
            authentication,
            logger,
            "atproto_reauthentication_required").ConfigureAwait(false);
        atprotoMetrics.Record(
            AtprotoAuthenticationOperation.Refresh,
            outcome,
            Stopwatch.GetElapsedTime(started));
        return result;
    }

    private async Task<IResult> RequireOnboardingReauthenticationAsync(
        HttpContext context,
        AuthenticateResult authentication,
        ILogger logger,
        string reason)
    {
        ClearCircuitTokenState(context, authentication.Principal, logger, reason);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Json(
            new { refreshed = false, reason = "reauthentication_required" },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static bool IsOnboardingSessionAllowed(
        ClaimsPrincipal principal,
        BffOnboardingStatus initialStatus,
        BffOnboardingStatus refreshedStatus,
        bool hasAdminAuthority)
    {
        return initialStatus.Disposition switch
        {
            BffOnboardingDisposition.Completed =>
                refreshedStatus.Disposition == BffOnboardingDisposition.Completed,
            BffOnboardingDisposition.InteractivePending =>
                refreshedStatus.Disposition is BffOnboardingDisposition.InteractivePending
                    or BffOnboardingDisposition.Completed,
            BffOnboardingDisposition.ConfiguredAdministratorPending =>
                HasMatchingConfiguredProvider(principal, initialStatus)
                && refreshedStatus.Disposition == BffOnboardingDisposition.Completed
                && hasAdminAuthority,
            BffOnboardingDisposition.Closed => false,
            _ => false
        };
    }

    private static bool HasMatchingConfiguredProvider(
        ClaimsPrincipal principal,
        BffOnboardingStatus status)
    {
        var providers = principal.FindAll("auth_provider").Take(2).ToArray();
        return providers.Length == 1 && status.AllowsProvider(providers[0].Value);
    }

    private bool TryCreateAtprotoRequestContext(
        HttpContext context,
        AuthenticateResult authentication,
        out AtprotoRequestContext session)
    {
        session = default;
        if (!authentication.Succeeded
            || authentication.Principal is not { } principal
            || authentication.Properties is not { } properties
            || !HasSingleClaim(principal, "auth_provider", "atproto")
            || !principal.TryGetOpaqueProviderSubject(out var subject)
            || !Guid.TryParse(subject.Value, out var userId)
            || userId == Guid.Empty
            || !TryGetSingleClaim(principal, "did", out var did)
            || !TryGetSingleClaim(principal, "tenant_id", out var tenantValue)
            || !Guid.TryParse(tenantValue, out var tenantId)
            || tenantId == Guid.Empty
            || properties.GetTokenValue("access_token") is not { } accessToken
            || string.IsNullOrWhiteSpace(accessToken)
            || accessToken.Length > MaximumPlatformTokenBytes)
        {
            return false;
        }

        try
        {
            var binding = tenantOriginResolver.Resolve(context.Request);
            if (binding.TenantId != tenantId)
            {
                return false;
            }

            session = new(tenantId, userId, did, binding.TenantSlug, accessToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private HttpRequestMessage CreateAtprotoRequest(HttpMethod method, AtprotoRequestContext session)
    {
        var request = new HttpRequestMessage(method, AtprotoBootstrapAssertionService.SessionBridgePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Tenant-Slug", session.TenantSlug);
        request.Headers.TryAddWithoutValidation(
            AtprotoBootstrapAssertionService.SessionBridgeHeaderName,
            assertionService.IssueSessionBridge(session.TenantId, session.UserId, session.Did, method));
        return request;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maximumBytes, 4096));
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("ATProto refresh response is too large.");
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static bool HasSingleClaim(ClaimsPrincipal principal, string type, string expected) =>
        TryGetSingleClaim(principal, type, out var actual)
        && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool TryGetSingleClaim(ClaimsPrincipal principal, string type, out string value)
    {
        var claims = principal.FindAll(type).Take(2).ToArray();
        value = claims.Length == 1 ? claims[0].Value : string.Empty;
        return claims.Length == 1;
    }

    private readonly record struct AtprotoRequestContext(
        Guid TenantId,
        Guid UserId,
        string Did,
        string TenantSlug,
        string AccessToken);

    private sealed record AtprotoRefreshBridgeResponse(
        Guid UserId,
        string Did,
        string AccessToken,
        DateTimeOffset ExpiresAt);
}
