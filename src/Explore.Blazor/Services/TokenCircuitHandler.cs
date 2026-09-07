using Event.Web.BffHosting.Security;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Explore.Blazor.Services;

public class TokenCircuitHandler : CircuitHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICircuitAccessTokenService _circuitAccessTokenService;
    private readonly ICircuitUserContext _circuitUserContext;
    private readonly IBffAuthCookieStore _bffAuthCookieStore;
    private readonly ILogger<TokenCircuitHandler> _logger;

    public TokenCircuitHandler(
        IHttpContextAccessor httpContextAccessor,
        ICircuitAccessTokenService circuitAccessTokenService,
        ICircuitUserContext circuitUserContext,
        IBffAuthCookieStore bffAuthCookieStore,
        ILogger<TokenCircuitHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _circuitAccessTokenService = circuitAccessTokenService;
        _circuitUserContext = circuitUserContext;
        _bffAuthCookieStore = bffAuthCookieStore;
        _logger = logger;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        CaptureCookieHeader(httpContext);

        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogDebug("[TokenCircuitHandler] No authenticated user during circuit open - skipping identity/token capture");
            return Task.CompletedTask;
        }

        try
        {
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

            // Strategy 1: Read from HttpContext.Items where CaptureAccessTokenAsync middleware stored it.
            // This is the same hybrid pattern used by TenantRouteContextAccessor.
            string? token = null;
            if (httpContext.Items.TryGetValue("AccessToken", out var itemValue) && itemValue is string itemToken)
            {
                token = itemToken;
                _logger.LogDebug("[TokenCircuitHandler] Token lookup completed | Outcome={Outcome} Source={Source} Purpose={Purpose}",
                    "found", "request_context", "circuit");
            }

            // Strategy 2: Fall back to GetTokenAsync if middleware didn't capture it.
            if (string.IsNullOrEmpty(token))
            {
                token = httpContext.GetTokenAsync("access_token").GetAwaiter().GetResult();
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
            _logger.LogWarning(
                "[TokenCircuitHandler] Token capture failed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                "rejected", "capture_exception", "circuit");
        }

        return Task.CompletedTask;
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
    {
        return async context =>
        {
            CaptureCookieHeader(_httpContextAccessor.HttpContext);
            using var userScope = _circuitUserContext.BeginActivityScope();
            using var cookieScope = _bffAuthCookieStore.BeginActivityScope();
            await next(context);
        };
    }
}
