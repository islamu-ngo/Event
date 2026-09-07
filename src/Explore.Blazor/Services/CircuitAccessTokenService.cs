// ABOUTME: Manages circuit-scoped token storage, irreversible session revocation, and API token forwarding.
// ABOUTME: Preserves exact-session cleanup and prevents revoked circuits from adopting another session's token.

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Explore.Blazor.Services;

public interface ICircuitAccessTokenService
{
    string? AccessToken { get; }
    void SetToken(string? token);
    void ClearToken();
    void RevokeSession(EventBffOpaqueIdentity? originalSubject, EventBffOpaqueIdentity? originalSession);
}

public interface ISetupSecretSessionService
{
    void SetForUser(string userId, string secret);
    string? GetForUser(string userId);
    void ClearForUser(string userId);
    string CreateAnonymousSession(string secret);
    string? GetForAnonymousSession(string sessionId);
    void ClearAnonymousSession(string sessionId);
}

public sealed class SetupSecretSessionService : ISetupSecretSessionService
{
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, SecretEntry> _userStore = new();
    private readonly ConcurrentDictionary<string, SecretEntry> _anonymousStore = new();
    private readonly TimeProvider _timeProvider;

    public SetupSecretSessionService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void SetForUser(string userId, string secret)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        _userStore[userId] = new SecretEntry(secret.Trim(), now);
        CleanupExpiredEntries(now);
    }

    public string? GetForUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return GetAndRefresh(_userStore, userId);
    }

    public void ClearForUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        _userStore.TryRemove(userId, out _);
    }

    public string CreateAnonymousSession(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Empty;
        }

        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = _timeProvider.GetUtcNow();
        _anonymousStore[sessionId] = new SecretEntry(secret.Trim(), now);
        CleanupExpiredEntries(now);
        return sessionId;
    }

    public string? GetForAnonymousSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return GetAndRefresh(_anonymousStore, sessionId);
    }

    public void ClearAnonymousSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _anonymousStore.TryRemove(sessionId, out _);
    }

    private string? GetAndRefresh(ConcurrentDictionary<string, SecretEntry> store, string key)
    {
        while (store.TryGetValue(key, out var entry))
        {
            var now = _timeProvider.GetUtcNow();
            if (entry.LastAccessedUtc < now - SessionIdleTimeout)
            {
                if (TryRemove(store, key, entry))
                {
                    return null;
                }

                continue;
            }

            if (store.TryUpdate(key, entry with { LastAccessedUtc = now }, entry))
            {
                return entry.Secret;
            }
        }

        return null;
    }

    private void CleanupExpiredEntries(DateTimeOffset now)
    {
        var cutoff = now - SessionIdleTimeout;
        foreach (var entry in _userStore.Where(kvp => kvp.Value.LastAccessedUtc < cutoff).ToList())
        {
            TryRemove(_userStore, entry.Key, entry.Value);
        }

        foreach (var entry in _anonymousStore.Where(kvp => kvp.Value.LastAccessedUtc < cutoff).ToList())
        {
            TryRemove(_anonymousStore, entry.Key, entry.Value);
        }
    }

    private static bool TryRemove(
        ConcurrentDictionary<string, SecretEntry> store,
        string key,
        SecretEntry entry) =>
        ((ICollection<KeyValuePair<string, SecretEntry>>)store).Remove(new KeyValuePair<string, SecretEntry>(key, entry));

    private sealed record SecretEntry(string Secret, DateTimeOffset LastAccessedUtc);
}

/// <summary>
/// Scoped circuit access token service that bridges the per-circuit token into the bounded
/// <see cref="ICircuitTokenStore"/>. Each Blazor circuit receives its own scoped instance,
/// but all instances share the singleton <see cref="ICircuitTokenStore"/> for cross-scope
/// token resolution (e.g., pooled HttpClient handlers).
/// </summary>
public class CircuitAccessTokenService : ICircuitAccessTokenService
{
    private readonly ICircuitTokenStore _tokenStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CircuitAccessTokenService> _logger;
    private string? _localToken;
    private string? _userId;
    private string? _sessionId;
    private readonly object _tokenSync = new();
    private bool _revoked;

    public CircuitAccessTokenService(
        ICircuitTokenStore tokenStore,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CircuitAccessTokenService> logger)
    {
        _tokenStore = tokenStore;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public string? AccessToken
    {
        get
        {
            lock (_tokenSync)
            {
                if (_revoked)
                {
                    return null;
                }

                var userId = _userId ?? GetUserIdFromHttpContext();
                var sessionId = _sessionId ?? GetSessionIdFromHttpContext();

                if (!string.IsNullOrEmpty(userId))
                {
                    var resolution = _tokenStore.Resolve(userId, sessionId);
                    if (!resolution.Found)
                    {
                        resolution = _tokenStore.ResolveByUserId(userId);
                    }

                    if (resolution.Found)
                    {
                        if (!string.IsNullOrEmpty(_localToken) && !string.Equals(_localToken, resolution.Token, StringComparison.Ordinal))
                        {
                            _logger.LogInformation(
                                "[CircuitAccessTokenService] Token resolution completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                                "replaced", "stale_local_token", "circuit");
                        }

                        return resolution.Token;
                    }
                }

                if (!CircuitTokenStore.IsTokenUsable(_localToken))
                {
                    _localToken = null;
                    return null;
                }

                return _localToken;
            }
        }
    }

    public void SetToken(string? token)
    {
        lock (_tokenSync)
        {
            if (_revoked)
            {
                return;
            }

            if (string.IsNullOrEmpty(token))
            {
                ClearToken();
                return;
            }

            if (!CircuitTokenStore.IsTokenUsable(token))
            {
                _localToken = null;
                _logger.LogWarning("[CircuitAccessTokenService] SetToken ignored an expired or near-expiry access token");
                return;
            }

            _localToken = token;

            // The forwarding handler resolves by the authenticated BFF principal. That id can
            // differ from the JWT subject after local-user/admin enrichment.
            var principalUserId = GetUserIdFromHttpContext();
            var principalSessionId = GetSessionIdFromHttpContext();

            if (!string.IsNullOrEmpty(principalUserId))
            {
                _userId = principalUserId;
                _sessionId = principalSessionId;
                var result = _tokenStore.Store(principalUserId, principalSessionId, token);
                if (!result.Accepted)
                {
                    _logger.LogDebug(
                        "[CircuitAccessTokenService] Token store rejected token: {RejectionCode}",
                        result.RejectionCode);
                }
            }
            else
            {
                _logger.LogWarning("[CircuitAccessTokenService] Token store skipped | Outcome={Outcome} Reason={Reason} Purpose={Purpose}", "skipped", "trusted_principal_unavailable", "circuit");
            }
        }
    }

    public void ClearToken()
    {
        lock (_tokenSync)
        {
            if (_revoked)
            {
                return;
            }

            var userId = _userId ?? GetUserIdFromHttpContext();
            var sessionId = _sessionId ?? GetSessionIdFromHttpContext();

            _localToken = null;
            _userId = null;
            _sessionId = null;

            if (!string.IsNullOrWhiteSpace(userId))
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    _tokenStore.ClearUser(userId);
                }
                else
                {
                    _tokenStore.ClearSession(userId, sessionId);
                }
            }
        }
    }

    public void RevokeSession(EventBffOpaqueIdentity? originalSubject, EventBffOpaqueIdentity? originalSession)
    {
        lock (_tokenSync)
        {
            if (_revoked)
            {
                return;
            }

            _revoked = true;
            _localToken = null;
            _userId = null;
            _sessionId = null;
            if (originalSubject is { Purpose: EventBffOpaqueIdentityPurpose.CircuitSubject,
                    Source: EventBffOpaqueIdentitySource.ProviderSubject } subject
                && originalSession is { Purpose: EventBffOpaqueIdentityPurpose.SessionId,
                    Source: EventBffOpaqueIdentitySource.SessionId } session
                && string.Equals(subject.AuthenticationScheme, session.AuthenticationScheme, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(subject.AuthenticationScheme)
                && !string.IsNullOrWhiteSpace(subject.Value)
                && !string.IsNullOrWhiteSpace(session.Value)
                && !subject.Value.Any(char.IsControl)
                && !session.Value.Any(char.IsControl))
            {
                _tokenStore.ClearSession(subject.PartitionKey, session.PartitionKey);
            }
        }
    }

    private string? GetUserIdFromHttpContext()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        return principal.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;
    }

    private string? GetSessionIdFromHttpContext()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        return principal.TryGetSessionId(out var sessionId) ? sessionId.PartitionKey : null;
    }

    private static string? TryResolveUserId(ClaimsPrincipal? user)
    {
        return user.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;
    }
}

/// <summary>
/// HTTP message handler that forwards the access token and trusted tenant slug to API requests.
/// Resolves token for the current authenticated user only via the bounded <see cref="ICircuitTokenStore"/>.
/// </summary>
public class AccessTokenForwardingHandler : DelegatingHandler
{
    private const string BffSelfClientName = "BffSelfClient";
    private const string InternalRefreshPath = "/bff/auth/refresh-session/internal";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICircuitAccessTokenService _circuitAccessTokenService;
    private readonly ICircuitUserContext _circuitUserContext;
    private readonly ICircuitTokenStore _tokenStore;
    private readonly ILogger<AccessTokenForwardingHandler> _logger;
    private readonly IHttpClientFactory? _bffSelfClientFactory;

    public AccessTokenForwardingHandler(
        IHttpContextAccessor httpContextAccessor,
        ICircuitAccessTokenService circuitAccessTokenService,
        ICircuitUserContext circuitUserContext,
        ICircuitTokenStore tokenStore,
        ILogger<AccessTokenForwardingHandler> logger,
        IHttpClientFactory? bffSelfClientFactory = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _circuitAccessTokenService = circuitAccessTokenService;
        _circuitUserContext = circuitUserContext;
        _tokenStore = tokenStore;
        _logger = logger;
        _bffSelfClientFactory = bffSelfClientFactory;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var routeClass = BffLogRouteClassifier.Classify(request.RequestUri);
        _logger.LogDebug("[AccessTokenForwardingHandler] Request processing started | Outcome={Outcome} RouteClass={RouteClass}",
            "started", routeClass);

        string? token = null;
        string? nearExpiryHttpContextToken = null;
        string source = "none";

        // Strategy 1: Try to get token from HttpContext (works during initial HTTP request)
        var httpContext = _httpContextAccessor.HttpContext;
        var isAuthenticated = httpContext?.User?.Identity?.IsAuthenticated == true;

        if (isAuthenticated)
        {
            try
            {
                token = await httpContext!.GetTokenAsync("access_token");
                if (!string.IsNullOrEmpty(token))
                {
                    if (CircuitTokenStore.IsTokenUsable(token))
                    {
                        source = "HttpContext";
                        _logger.LogDebug("[AccessTokenForwardingHandler] Got token from HttpContext");
                    }
                    else if (CircuitTokenStore.IsTokenForwardable(token))
                    {
                        nearExpiryHttpContextToken = token;
                        _logger.LogInformation(
                            "[AccessTokenForwardingHandler] Token assessment completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                            "deferred", "access_token_near_expiry", routeClass);
                        token = null;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[AccessTokenForwardingHandler] Token assessment completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                            "rejected", "access_token_expired", routeClass);
                        token = null;
                    }
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("[AccessTokenForwardingHandler] Token lookup completed | Outcome={Outcome} Reason={Reason}", "rejected", "request_token_exception");
            }
        }

        // Strategy 1b: If the HttpContext token was missing or not usable, try to refresh
        // the cookie session explicitly. This runs CookieAuthenticationEvents.ValidatePrincipal,
        // which can exchange the stored refresh_token for a new access_token and re-issue the
        // cookie. The refreshed token is also pushed into the circuit token store so later
        // SignalR/circuit-dispatched requests can reuse it.
        if (string.IsNullOrEmpty(token) && isAuthenticated && httpContext is not null)
        {
            try
            {
                var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (authResult.Succeeded && authResult.Properties is not null)
                {
                    var refreshedToken = authResult.Properties.GetTokenValue("access_token");
                    if (!string.IsNullOrEmpty(refreshedToken) && CircuitTokenStore.IsTokenUsable(refreshedToken))
                    {
                        _circuitAccessTokenService.SetToken(refreshedToken);
                        token = refreshedToken;
                        source = "HttpContextRefreshed";
                        _logger.LogInformation(
                            "[AccessTokenForwardingHandler] Token refresh completed | Outcome={Outcome} RouteClass={RouteClass}",
                            "refreshed", routeClass);
                    }
                    else if (!string.IsNullOrEmpty(refreshedToken))
                    {
                        _logger.LogInformation(
                            "[AccessTokenForwardingHandler] Token refresh completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                            "rejected", "refreshed_token_unusable", routeClass);
                    }
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("[AccessTokenForwardingHandler] Token refresh completed | Outcome={Outcome} Reason={Reason}", "rejected", "cookie_authentication_exception");
            }
        }

        // Strategy 2: Try to get token from bounded store by current user ID
        if (string.IsNullOrEmpty(token))
        {
            var userId = TryResolveUserId(httpContext?.User);

            if (!string.IsNullOrEmpty(userId))
            {
                var sessionId = httpContext?.User.TryGetSessionId(out var resolvedSessionId) == true ? resolvedSessionId.PartitionKey : null;
                var resolution = _tokenStore.Resolve(userId, sessionId);
                if (resolution.Found)
                {
                    token = resolution.Token;
                    source = "TokenStore(userId)";
                }
                else
                {
                    resolution = _tokenStore.ResolveByUserId(userId);
                    if (resolution.Found)
                    {
                        token = resolution.Token;
                        source = "TokenStore(userId-only)";
                        _logger.LogInformation(
                            "[AccessTokenForwardingHandler] Token lookup completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                            "resolved", "subject_fallback", "forwarding");
                    }
                }
            }
            else if (isAuthenticated)
            {
                _logger.LogWarning(
                    "[AccessTokenForwardingHandler] Identity resolution completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                    "rejected", "identity_unavailable", routeClass);
            }
        }

        // Strategy 3: Fall back to the scoped ICircuitAccessTokenService which retains the circuit's user
        // identity even when HttpContext is unavailable (e.g., Blazor Server click events dispatched via
        // SignalR where IHttpContextAccessor.HttpContext is null).
        if (string.IsNullOrEmpty(token))
        {
            token = _circuitAccessTokenService.AccessToken;
            if (!string.IsNullOrEmpty(token))
            {
                source = "CircuitAccessTokenService";
                _logger.LogDebug(
                    "[AccessTokenForwardingHandler] Token lookup completed | Outcome={Outcome} Purpose={Purpose} SubjectPresent={SubjectPresent}",
                    "resolved", "forwarding", !string.IsNullOrWhiteSpace(_circuitUserContext.UserId ?? TryResolveUserId(httpContext?.User)));
            }
        }

        // Strategy 4: Use AsyncLocal-backed ICircuitUserContext to get the userId and look up
        // the token in the bounded store. This works across DI scope boundaries where HttpContext
        // is null but the Blazor circuit async context still flows.
        if (string.IsNullOrEmpty(token))
        {
            var userId = _circuitUserContext.UserId;
            if (!string.IsNullOrEmpty(userId))
            {
                var resolution = _tokenStore.Resolve(userId, _circuitUserContext.SessionId);
                if (resolution.Found)
                {
                    token = resolution.Token;
                    source = "CircuitUserContext";
                    _logger.LogDebug("[AccessTokenForwardingHandler] Got token from store via CircuitUserContext");
                }
                else
                {
                    resolution = _tokenStore.ResolveByUserId(userId);
                    if (resolution.Found)
                    {
                        token = resolution.Token;
                        source = "CircuitUserContext(userId-only)";
                        _logger.LogInformation(
                            "[AccessTokenForwardingHandler] Token lookup completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                            "resolved", "circuit_subject_fallback", "forwarding");
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(token) && isAuthenticated && httpContext is not null)
        {
            token = await TryRefreshViaBffSelfEndpointAsync(httpContext, request, cancellationToken);
            if (!string.IsNullOrEmpty(token))
            {
                source = "BffSelfRefresh";
            }
        }

        // Strategy 5: If the only token available is still valid but inside the persistence
        // safety buffer, forward it for this short request instead of dropping auth entirely.
        if (string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(nearExpiryHttpContextToken))
        {
            token = nearExpiryHttpContextToken;
            source = "HttpContextNearExpiry";
            _logger.LogInformation(
                "[AccessTokenForwardingHandler] Token selection completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                "selected", "near_expiry_fallback", routeClass);
        }

        // Add Authorization header if we have a token
        if (!string.IsNullOrEmpty(token) && !request.Headers.Contains("Authorization"))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            _logger.LogInformation(
                "[AccessTokenForwardingHandler] Authorization forwarding completed | Outcome={Outcome} Source={Source} RouteClass={RouteClass}",
                "forwarded",
                source,
                routeClass);
        }
        else if (!string.IsNullOrEmpty(token))
        {
            _logger.LogDebug(
                "[AccessTokenForwardingHandler] Authorization forwarding completed | Outcome={Outcome} Source={Source} RouteClass={RouteClass}",
                "preserved", source, routeClass);
        }
        else if (string.IsNullOrEmpty(token))
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (IsAnonymousAllowedPath(path))
            {
                _logger.LogDebug("[AccessTokenForwardingHandler] Token selection completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                    "not_required", "anonymous_route", routeClass);
            }
            else
            {
                _logger.LogWarning(
                    "[AccessTokenForwardingHandler] Token selection completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                    "not_found", "access_token_unavailable", routeClass);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string?> TryRefreshViaBffSelfEndpointAsync(
        HttpContext httpContext,
        HttpRequestMessage outboundRequest,
        CancellationToken cancellationToken)
    {
        if (_bffSelfClientFactory is null)
        {
            return null;
        }

        try
        {
            var selfClient = _bffSelfClientFactory.CreateClient(BffSelfClientName);
            using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, BuildInternalRefreshUri(httpContext));
            using var response = await selfClient.SendAsync(refreshRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[AccessTokenForwardingHandler] Self refresh completed | Outcome={Outcome} StatusCode={StatusCode} RouteClass={RouteClass}",
                    "rejected",
                    (int)response.StatusCode,
                    BffLogRouteClassifier.Classify(outboundRequest.RequestUri));
                return null;
            }

            var userId = TryResolveUserId(httpContext.User) ?? _circuitUserContext.UserId;
            var sessionId = httpContext.User.TryGetSessionId(out var resolvedSessionId)
                ? resolvedSessionId.PartitionKey : _circuitUserContext.SessionId;
            var refreshedToken = ResolveTokenFromStore(userId, sessionId);
            if (!string.IsNullOrEmpty(refreshedToken))
            {
                _circuitAccessTokenService.SetToken(refreshedToken);
                _logger.LogInformation(
                    "[AccessTokenForwardingHandler] Self refresh completed | Outcome={Outcome} RouteClass={RouteClass}",
                    "refreshed", BffLogRouteClassifier.Classify(outboundRequest.RequestUri));
                return refreshedToken;
            }

            _logger.LogWarning(
                "[AccessTokenForwardingHandler] Self refresh completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                "not_found", "circuit_token_unavailable", BffLogRouteClassifier.Classify(outboundRequest.RequestUri));
            return null;
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "[AccessTokenForwardingHandler] Self refresh completed | Outcome={Outcome} Reason={Reason} RouteClass={RouteClass}",
                "rejected", "self_refresh_exception", BffLogRouteClassifier.Classify(outboundRequest.RequestUri));
            return null;
        }
    }

    private string? ResolveTokenFromStore(string? userId, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var resolution = _tokenStore.Resolve(userId, sessionId);
        if (!resolution.Found)
        {
            resolution = _tokenStore.ResolveByUserId(userId);
        }

        return resolution.Found ? resolution.Token : null;
    }

    private static Uri BuildInternalRefreshUri(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        var refreshPath = string.Concat(pathBase, InternalRefreshPath);

        return new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? -1, refreshPath).Uri;
    }

    private static bool IsAnonymousAllowedPath(string pathAndQuery)
    {
        return pathAndQuery.Contains("/api/PublicExperience/settings", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/PublicExperience/shell", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/InstanceOnboarding/status", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/InstanceOnboarding/validate-secret", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/InstanceOnboarding/auth-provider-configuration/internal", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/InstanceOnboarding/auth-provider-configuration", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/InstanceOnboarding/authz-provider-configuration/internal", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/InstanceOnboarding/authz-provider-configuration", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/InstanceOnboarding/auth-provider-configured", StringComparison.OrdinalIgnoreCase)
            || pathAndQuery.Contains("/api/translation", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryResolveUserId(ClaimsPrincipal? user)
    {
        return user.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;
    }
}
