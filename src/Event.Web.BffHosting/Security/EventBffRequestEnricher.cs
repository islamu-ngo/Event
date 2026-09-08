using System.Net.Http.Headers;
using Event.Web.BffHosting.Abstractions;
using Microsoft.AspNetCore.Authentication;

namespace Event.Web.BffHosting.Security;

public sealed class EventBffRequestEnricher(
    IEventBffAccessTokenProvider accessTokenProvider,
    IEventBffTenantHintProvider tenantHintProvider,
    IEventBffSetupSecretProvider setupSecretProvider,
    IEventBffSupportAccessProvider supportAccessProvider)
{
    public async ValueTask<EventBffTrustedRequest> ResolveForProxyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var accessToken = await accessTokenProvider.ResolveAccessTokenAsync(httpContext, cancellationToken);
        return await ResolveAsync(httpContext, accessToken, cancellationToken);
    }

    public async ValueTask<EventBffTrustedRequest> ResolveForSessionAsync(
        HttpContext httpContext,
        AuthenticateResult session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(session.Principal);
        var accessToken = session.Properties?.GetTokenValue("access_token");
        var originalPrincipal = httpContext.User;
        httpContext.User = session.Principal;

        try
        {
            return await ResolveAsync(httpContext, accessToken, cancellationToken);
        }
        finally
        {
            httpContext.User = originalPrincipal;
        }
    }

    private async ValueTask<EventBffTrustedRequest> ResolveAsync(
        HttpContext httpContext,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (EventBffRequestPolicy.IsAnonymousOnboardingPath(httpContext.Request.Path))
        {
            accessToken = null;
        }

        var setupSecret = EventBffRequestPolicy.RequiresSetupSecret(
            httpContext.Request.Method,
            httpContext.Request.Path)
            ? await setupSecretProvider.ResolveSetupSecretAsync(httpContext, cancellationToken)
            : null;
        var supportAccessSessionId = await supportAccessProvider.ResolveSupportAccessSessionIdAsync(
            httpContext,
            cancellationToken);

        return new EventBffTrustedRequest(
            EventBffTokenSafety.IsTokenForwardable(accessToken) ? accessToken : null,
            tenantHintProvider.ResolveTenantSlug(httpContext),
            setupSecret,
            supportAccessSessionId);
    }
}

public sealed class EventBffTrustedRequest(
    string? accessToken,
    string? tenantSlug,
    string? setupSecret,
    string? supportAccessSessionId)
{
    public string? AccessToken { get; } = accessToken;
    public string? TenantSlug { get; } = tenantSlug;
    public string? SetupSecret { get; } = setupSecret;
    public string? SupportAccessSessionId { get; } = supportAccessSessionId;

    public void ApplyTo(HttpRequestMessage request)
    {
        BffProxyHeaderSanitizer.RemoveBrowserControlledHeaders(request);

        if (AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        Add(request, EventBffHeaderNames.TenantSlug, TenantSlug);
        Add(request, EventBffHeaderNames.SetupSecret, SetupSecret);
        Add(request, EventBffHeaderNames.SupportAccessSessionId, SupportAccessSessionId);
    }

    public void ApplyTo(HttpRequest request)
    {
        BffProxyHeaderSanitizer.RemoveBrowserControlledHeaders(request);

        if (AccessToken is not null)
        {
            request.Headers.Authorization = $"Bearer {AccessToken}";
        }

        Add(request, EventBffHeaderNames.TenantSlug, TenantSlug);
        Add(request, EventBffHeaderNames.SetupSecret, SetupSecret);
        Add(request, EventBffHeaderNames.SupportAccessSessionId, SupportAccessSessionId);
    }

    private static void Add(HttpRequestMessage request, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static void Add(HttpRequest request, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers[name] = value;
        }
    }
}

public static class EventBffRequestPolicy
{
    public static bool RequiresAntiforgeryValidation(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && IsUnsafeMethod(request.Method)
            && request.Cookies.Count > 0;
    }

    public static bool IsAnonymousOnboardingPath(PathString path)
    {
        if (!path.StartsWithSegments("/api/InstanceOnboarding", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Value is null
            || !path.Value.EndsWith("/complete", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresSetupSecret(string method, PathString path)
    {
        if (HttpMethods.IsGet(method)
                && string.Equals(path.Value, "/api/instanceonboarding/status", StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsPost(method)
                && string.Equals(path.Value, "/api/instanceonboarding/complete-local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsPatch(method)
            && (string.Equals(
                    path.Value,
                    "/api/instance/settings/auth-provider",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    path.Value,
                    "/api/instance/settings/authz-provider",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return path.StartsWithSegments("/api/InstanceOnboarding/complete", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/InstanceOnboarding/validate-secret", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments(
                "/api/InstanceOnboarding/auth-provider-configuration/keycloak-bootstrap",
                StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments(
                "/api/InstanceOnboarding/auth-provider-configuration",
                StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments(
                "/api/InstanceOnboarding/authz-provider-configuration",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeMethod(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);
}
