using Event.Web.BffHosting.Security;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Security.Claims;

namespace Explore.Blazor.Services;

public sealed class TokenCircuitHandler : CircuitHandler, IDisposable
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICircuitAccessTokenService _circuitAccessTokenService;
    private readonly ICircuitUserContext _circuitUserContext;
    private readonly IBffAuthCookieStore _bffAuthCookieStore;
    private readonly ILogger<TokenCircuitHandler> _logger;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IHostEnvironmentAuthenticationStateProvider _hostAuthenticationStateProvider;
    private readonly BffAdminClaimsTransformation _adminClaimsTransformation;
    private readonly CancellationTokenSource _lifetimeCancellation;
    private readonly CancellationToken _lifetimeToken;
    private ClaimsPrincipal? _originalPrincipal;
    private AuthenticationProperties? _originalProperties;
    private string? _originalAccessToken;
    private EventBffOpaqueIdentity? _originalSubject;
    private EventBffOpaqueIdentity? _originalSession;
    private int _revoked;
    private int _disposed;
    private int _openingCompleted;

    public TokenCircuitHandler(
        IHttpContextAccessor httpContextAccessor,
        ICircuitAccessTokenService circuitAccessTokenService,
        ICircuitUserContext circuitUserContext,
        IBffAuthCookieStore bffAuthCookieStore,
        ILogger<TokenCircuitHandler> logger,
        AuthenticationStateProvider authenticationStateProvider,
        BffAdminClaimsTransformation adminClaimsTransformation)
    {
        _httpContextAccessor = httpContextAccessor;
        _circuitAccessTokenService = circuitAccessTokenService;
        _circuitUserContext = circuitUserContext;
        _bffAuthCookieStore = bffAuthCookieStore;
        _logger = logger;
        _authenticationStateProvider = authenticationStateProvider;
        _hostAuthenticationStateProvider = authenticationStateProvider as IHostEnvironmentAuthenticationStateProvider
            ?? throw new InvalidOperationException("The circuit authentication provider must support host state replacement.");
        _adminClaimsTransformation = adminClaimsTransformation;
        _lifetimeCancellation = new CancellationTokenSource();
        _lifetimeToken = _lifetimeCancellation.Token;
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        try
        {
            CaptureCookieHeader(httpContext);
            var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated != true)
            {
                _logger.LogDebug("[TokenCircuitHandler] No authenticated user during circuit open - skipping identity/token capture");
                return;
            }

            _originalPrincipal = new ClaimsPrincipal(state.User.Identities.Select(identity => new ClaimsIdentity(identity)));
            _originalSubject = _originalPrincipal.TryGetCircuitSubject(out var originalSubject) ? originalSubject : null;
            _originalSession = _originalPrincipal.TryGetSessionId(out var originalSession) ? originalSession : null;
            if (httpContext is null)
            {
                return;
            }

            var authentication = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _originalProperties = authentication.Properties is { } properties
                ? new AuthenticationProperties(new Dictionary<string, string?>(properties.Items))
                : null;
            _originalAccessToken = authentication.Properties?.GetTokenValue("access_token");
            cancellationToken.ThrowIfCancellationRequested();

            // Capture userId into AsyncLocal-backed context so AccessTokenForwardingHandler
            // can resolve it even when running in a different DI scope.
            if (httpContext.User.TryGetCircuitSubject(out var userId))
            {
                _circuitUserContext.SetUserId(userId.PartitionKey);
                var sessionPresent = httpContext.User.TryGetSessionId(out var sessionId);
                _circuitUserContext.SetSessionId(sessionPresent ? sessionId.PartitionKey : null);
                _logger.LogDebug(
                    "[TokenCircuitHandler] Identity capture completed | Outcome={Outcome} Purpose={Purpose} SessionPresent={SessionPresent}",
                    "accepted", "circuit", sessionPresent);
            }

            // Preserve existing capture fallbacks for ordinary forwarding, but never use
            // them to replace the original cookie token used by the authority probe.
            string? token = _originalAccessToken;
            if (string.IsNullOrEmpty(token)
                && httpContext.Items.TryGetValue("AccessToken", out var itemValue) && itemValue is string itemToken)
            {
                token = itemToken;
                _logger.LogDebug("[TokenCircuitHandler] Token lookup completed | Outcome={Outcome} Source={Source} Purpose={Purpose}",
                    "found", "request_context", "circuit");
            }

            // Fall back to the active HTTP scheme only for ordinary token capture.
            if (string.IsNullOrEmpty(token))
            {
                token = await httpContext.GetTokenAsync("access_token");
                if (!string.IsNullOrEmpty(token))
                {
                    _logger.LogDebug("[TokenCircuitHandler] Token lookup completed | Outcome={Outcome} Source={Source} Purpose={Purpose}",
                        "found", "authentication_properties", "circuit");
                }
            }

            if (!string.IsNullOrEmpty(token))
            {
                _circuitAccessTokenService.SetToken(token);
                _logger.LogDebug("[TokenCircuitHandler] Token capture completed | Outcome={Outcome} Purpose={Purpose} TokenPresent={TokenPresent}",
                    "accepted", "circuit", true);
            }
            else
            {
                _logger.LogDebug("[TokenCircuitHandler] Token lookup completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                    "not_found", "access_token_missing", "circuit");
            }
        }
        catch (Exception)
        {
            RevokeCircuit();
            _logger.LogWarning(
                "[TokenCircuitHandler] Token capture failed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                "rejected", "capture_exception", "circuit");
        }
        finally
        {
            Volatile.Write(ref _openingCompleted, 1);
        }
    }

    private void CaptureCookieHeader(HttpContext? httpContext)
    {
        // BffSelfClient uses the captured cookie header for server-side calls back through
        // BFF endpoints. First-run setup is unauthenticated, so capture this before the
        // authenticated-user token path exits.
        if (httpContext?.Request.Headers.TryGetValue(HeaderNames.Cookie, out var cookieHeader) != true)
        {
            return;
        }

        var cookieValue = cookieHeader.ToString();
        if (string.IsNullOrEmpty(cookieValue))
        {
            return;
        }

        _bffAuthCookieStore.SetCookieHeader(cookieValue);
        _logger.LogDebug("[TokenCircuitHandler] Cookie capture completed | Outcome={Outcome} Purpose={Purpose} CookiePresent={CookiePresent}",
            "accepted", "circuit", true);
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
        => context => InvokeInboundActivityAsync(context, next);

    private async Task InvokeInboundActivityAsync(
        CircuitInboundActivityContext context,
        Func<CircuitInboundActivityContext, Task> next)
    {
        if (Volatile.Read(ref _revoked) != 0)
        {
            return;
        }

        // The framework's initial inbound activity invokes OnCircuitOpenedAsync through
        // next. Only that initialization precedes capture; a completed open never bypasses validation.
        if (Volatile.Read(ref _openingCompleted) != 0)
        {
            var originalPrincipal = _originalPrincipal;
            var originalProperties = _originalProperties;
            var originalAccessToken = _originalAccessToken;
            try
            {
                var current = await _authenticationStateProvider.GetAuthenticationStateAsync();
                if (originalPrincipal is not null || current.User.Identity?.IsAuthenticated == true)
                {
                    if (originalPrincipal is null
                        || !MatchesOriginalSession(current.User)
                        || !await _adminClaimsTransformation.ValidateLocalSessionAsync(
                            principal: current.User,
                            properties: originalProperties,
                            accessToken: originalAccessToken,
                            cancellationToken: _lifetimeToken))
                    {
                        RevokeCircuit();
                        return;
                    }
                    var checkedState = await _authenticationStateProvider.GetAuthenticationStateAsync();
                    if (Volatile.Read(ref _revoked) != 0 || !MatchesOriginalSession(checkedState.User))
                    {
                        RevokeCircuit();
                        return;
                    }
                    _lifetimeToken.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
            {
                RevokeCircuit();
                return;
            }
        }

        if (Volatile.Read(ref _revoked) != 0 || _lifetimeToken.IsCancellationRequested)
        {
            return;
        }

        CaptureCookieHeader(_httpContextAccessor.HttpContext);
        using var userScope = _circuitUserContext.BeginActivityScope();
        using var cookieScope = _bffAuthCookieStore.BeginActivityScope();
        await next(context);
    }

    private bool MatchesOriginalSession(ClaimsPrincipal principal)
    {
        var originalPrincipal = _originalPrincipal;
        var originalProviders = originalPrincipal?.FindAll("auth_provider").Take(2).ToArray() ?? [];
        var currentProviders = principal.FindAll("auth_provider").Take(2).ToArray();
        if (originalProviders.Length > 1
            || currentProviders.Length != originalProviders.Length)
        {
            return false;
        }

        if (currentProviders is [var currentProvider] && originalProviders is [var originalProvider])
        {
            if (!string.Equals(currentProvider.Value, originalProvider.Value, StringComparison.Ordinal))
            {
                return false;
            }

            var currentMarkerIsAuthenticated = principal.Identities.Any(identity => identity.IsAuthenticated
                && identity.HasClaim("auth_provider", currentProvider.Value));
            var originalMarkerIsAuthenticated = originalPrincipal!.Identities.Any(identity => identity.IsAuthenticated
                && identity.HasClaim("auth_provider", originalProvider.Value));
            if (currentMarkerIsAuthenticated != originalMarkerIsAuthenticated)
            {
                return false;
            }
        }

        if (_originalSubject is not { } originalSubject
            || !principal.TryGetCircuitSubject(out var subject)
            || !string.Equals(subject.PartitionKey, originalSubject.PartitionKey, StringComparison.Ordinal))
        {
            return false;
        }

        var hasSession = principal.TryGetSessionId(out var session);
        return _originalSession is { } originalSession
            ? hasSession && string.Equals(session.PartitionKey, originalSession.PartitionKey, StringComparison.Ordinal)
            : !hasSession;
    }

    private void RevokeCircuit()
    {
        if (Interlocked.Exchange(ref _revoked, 1) != 0)
        {
            return;
        }

        _circuitAccessTokenService.RevokeSession(originalSubject: _originalSubject, originalSession: _originalSession);
        _circuitUserContext.Clear();
        _bffAuthCookieStore.Clear();
        _originalAccessToken = null;
        _originalProperties = null;
        _originalPrincipal = null;
        _hostAuthenticationStateProvider.SetAuthenticationState(
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _revoked, 1);
        try
        {
            _lifetimeCancellation.Cancel();
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _originalAccessToken = null;
            _originalProperties = null;
            _originalPrincipal = null;
            GC.SuppressFinalize(this);
        }
    }
}
