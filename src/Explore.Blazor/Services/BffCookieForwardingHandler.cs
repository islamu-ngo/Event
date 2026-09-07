using Explore.Blazor.Services.Auth;

using Microsoft.Net.Http.Headers;
namespace Explore.Blazor.Services;

public class BffCookieForwardingHandler : DelegatingHandler
{
    private const string AntiforgeryCookieName = "XSRF-TOKEN";
    private const string FrameworkAntiforgeryCookiePrefix = ".AspNetCore.Antiforgery.";
    private const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    private readonly IBffAuthCookieStore _bffAuthCookieStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<BffCookieForwardingHandler> _logger;
    private readonly IBffSelfCallTokenService? _selfCallTokenService;
    private readonly AtprotoBootstrapAssertionService? _atprotoBootstrapAssertionService;

    public BffCookieForwardingHandler(
        IBffAuthCookieStore bffAuthCookieStore,
        IHttpContextAccessor httpContextAccessor,
        ILogger<BffCookieForwardingHandler> logger,
        IBffSelfCallTokenService? selfCallTokenService = null,
        AtprotoBootstrapAssertionService? atprotoBootstrapAssertionService = null)
    {
        _bffAuthCookieStore = bffAuthCookieStore;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _selfCallTokenService = selfCallTokenService;
        _atprotoBootstrapAssertionService = atprotoBootstrapAssertionService;
    }

    public BffCookieForwardingHandler(
        IBffAuthCookieStore bffAuthCookieStore,
        ILogger<BffCookieForwardingHandler> logger)
        : this(bffAuthCookieStore, new HttpContextAccessor(), logger)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var currentRequestCookie = _httpContextAccessor.HttpContext?.Request.Headers.Cookie.ToString();
        var cookie = BuildForwardedCookieHeader(_bffAuthCookieStore.CookieHeader, currentRequestCookie);
        var antiforgeryHeader = TryGetCookieValue(cookie, AntiforgeryCookieName);

        if (!string.IsNullOrEmpty(cookie) && !request.Headers.Contains(HeaderNames.Cookie))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Cookie, cookie);
            _logger.LogDebug("[BffCookieForwardingHandler] Cookie forwarding completed | Outcome={Outcome} RouteClass={RouteClass} CookiePresent={CookiePresent}",
                "forwarded", BffLogRouteClassifier.Classify(request.RequestUri), true);
        }

        if (!string.IsNullOrWhiteSpace(antiforgeryHeader) && !request.Headers.Contains(AntiforgeryHeaderName))
        {
            request.Headers.TryAddWithoutValidation(AntiforgeryHeaderName, antiforgeryHeader);
        }

        TryAddSelfCallToken(request);
        AddServerBootstrapAssertion(request);

        return base.SendAsync(request, cancellationToken);
    }

    private void AddServerBootstrapAssertion(HttpRequestMessage request)
    {
        _ = request.Headers.Remove(AtprotoBootstrapAssertionService.HeaderName);
        if (_atprotoBootstrapAssertionService is null
            || request.Method != HttpMethod.Post
            || !IsExactPath(request.RequestUri, AtprotoBootstrapAssertionService.BridgePath)
            || !request.Options.TryGetValue(AtprotoBootstrapRequestOptions.TenantIdKey, out var tenantId)
            || !request.Options.TryGetValue(AtprotoBootstrapRequestOptions.DidKey, out var did)
            || !request.Options.TryGetValue(AtprotoBootstrapRequestOptions.ClassificationKey, out var classification)
            || tenantId == Guid.Empty)
        {
            return;
        }

        request.Options.TryGetValue(AtprotoBootstrapRequestOptions.CanonicalActorIdKey, out Guid? canonicalActorId);
        request.Options.TryGetValue(
            AtprotoBootstrapRequestOptions.ExpectedCanonicalActorConcurrencyStampKey,
            out Guid? expectedCanonicalActorConcurrencyStamp);

        request.Headers.TryAddWithoutValidation(
            AtprotoBootstrapAssertionService.HeaderName,
            _atprotoBootstrapAssertionService.Issue(
                tenantId,
                did,
                classification,
                request.Method,
                AtprotoBootstrapAssertionService.BridgePath,
                canonicalActorId,
                expectedCanonicalActorConcurrencyStamp));
    }

    private static bool IsExactPath(Uri? uri, string expectedPath)
    {
        if (uri is null)
        {
            return false;
        }

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?', 2)[0];
        return string.Equals(path, expectedPath, StringComparison.Ordinal);
    }

    private static bool IsMutatingMethod(HttpMethod method)
    {
        return method == HttpMethod.Post
            || method == HttpMethod.Put
            || method == HttpMethod.Patch
            || method == HttpMethod.Delete;
    }

    private void TryAddSelfCallToken(HttpRequestMessage request)
    {
        if (!IsMutatingMethod(request.Method)
            || !IsBffEndpoint(request.RequestUri)
            || _selfCallTokenService is null
            || request.Headers.Contains(BffSelfCallHeaders.Token))
        {
            return;
        }

        var token = _selfCallTokenService.Issue(_httpContextAccessor.HttpContext, request);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation(BffSelfCallHeaders.Token, token);
        }
    }

    private static bool IsBffEndpoint(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return false;
        }

        var path = requestUri.IsAbsoluteUri
            ? requestUri.AbsolutePath
            : requestUri.OriginalString;
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path.StartsWith("/bff/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetCookieValue(string? cookieHeader, string cookieName)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return null;
        }

        foreach (var segment in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = segment[..separatorIndex];
            if (!string.Equals(name, cookieName, StringComparison.Ordinal))
            {
                continue;
            }

            var value = segment[(separatorIndex + 1)..];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static string? BuildForwardedCookieHeader(string? circuitCookie, string? currentRequestCookie)
    {
        if (string.IsNullOrWhiteSpace(circuitCookie))
        {
            return string.IsNullOrWhiteSpace(currentRequestCookie) ? null : currentRequestCookie;
        }

        if (string.IsNullOrWhiteSpace(currentRequestCookie))
        {
            return circuitCookie;
        }

        var mergedCookies = ParseCookies(circuitCookie);
        foreach (var currentCookie in ParseCookies(currentRequestCookie))
        {
            var existingIndex = mergedCookies.FindIndex(cookie => string.Equals(cookie.Name, currentCookie.Name, StringComparison.Ordinal));
            if (existingIndex < 0)
            {
                mergedCookies.Add(currentCookie);
                continue;
            }

            if (IsAntiforgeryCookie(currentCookie.Name))
            {
                mergedCookies[existingIndex] = currentCookie;
            }
        }

        return string.Join("; ", mergedCookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }

    private static bool IsAntiforgeryCookie(string cookieName)
    {
        return string.Equals(cookieName, AntiforgeryCookieName, StringComparison.Ordinal)
            || cookieName.StartsWith(FrameworkAntiforgeryCookiePrefix, StringComparison.Ordinal);
    }

    private static List<CookieValue> ParseCookies(string cookieHeader)
    {
        var cookies = new List<CookieValue>();
        foreach (var segment in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = segment[..separatorIndex];
            var value = segment[(separatorIndex + 1)..];
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            cookies.Add(new CookieValue(name, value));
        }

        return cookies;
    }

    private readonly record struct CookieValue(string Name, string Value);
}

public static class AtprotoBootstrapRequestOptions
{
    internal static readonly HttpRequestOptionsKey<Guid> TenantIdKey = new("AtprotoBootstrapTenantId");
    internal static readonly HttpRequestOptionsKey<string> DidKey = new("AtprotoBootstrapDid");
    internal static readonly HttpRequestOptionsKey<string> ClassificationKey = new("AtprotoBootstrapClassification");
    internal static readonly HttpRequestOptionsKey<Guid?> CanonicalActorIdKey = new("AtprotoBootstrapCanonicalActorId");
    internal static readonly HttpRequestOptionsKey<Guid?> ExpectedCanonicalActorConcurrencyStampKey = new("AtprotoBootstrapExpectedCanonicalActorConcurrencyStamp");

    public static void Bind(
        HttpRequestMessage request,
        Guid tenantId,
        string did,
        string rawClassification,
        Guid? canonicalActorId = null,
        Guid? expectedCanonicalActorConcurrencyStamp = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (tenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(did)
            || !did.StartsWith("did:", StringComparison.Ordinal))
        {
            throw new ArgumentException("ATProto bootstrap binding is invalid.");
        }
        if (canonicalActorId.HasValue != expectedCanonicalActorConcurrencyStamp.HasValue
            || canonicalActorId == Guid.Empty
            || expectedCanonicalActorConcurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("ATProto canonical Actor target is invalid.");
        }

        request.Options.Set(TenantIdKey, tenantId);
        request.Options.Set(DidKey, did);
        request.Options.Set(ClassificationKey, AtprotoSubjectClassifications.Normalize(rawClassification));
        request.Options.Set(CanonicalActorIdKey, canonicalActorId);
        request.Options.Set(ExpectedCanonicalActorConcurrencyStampKey, expectedCanonicalActorConcurrencyStamp);
    }
}
