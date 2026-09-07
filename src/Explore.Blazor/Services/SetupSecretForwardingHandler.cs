// ABOUTME: DelegatingHandler that forwards trusted setup secret headers to onboarding API endpoints.
// ABOUTME: Strips client-controlled setup headers and resolves secrets through the BFF resolver only.

namespace Explore.Blazor.Services;

/// <summary>
/// Forwards the X-Setup-Secret header to API endpoints that require it during initial onboarding.
/// In Blazor circuit context (HttpContext null), falls back to extracting the user ID from
/// the Authorization header set by AccessTokenForwardingHandler, then looks up the secret
/// from SetupSecretSessionService.
/// </summary>
public class SetupSecretForwardingHandler : DelegatingHandler
{
    private readonly ISetupSecretResolver _setupSecretResolver;

    public SetupSecretForwardingHandler(ISetupSecretResolver setupSecretResolver)
    {
        _setupSecretResolver = setupSecretResolver;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        _ = request.Headers.Remove("X-Setup-Secret");

        if (!RequiresSetupSecret(request.Method.Method, path))
        {
            return base.SendAsync(request, cancellationToken);
        }

        var setupSecret = _setupSecretResolver.Resolve(outboundRequest: request);
        if (setupSecret.Found && !string.IsNullOrWhiteSpace(setupSecret.Secret))
        {
            request.Headers.Add("X-Setup-Secret", setupSecret.Secret);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool RequiresSetupSecret(string method, string path)
    {
        if ((HttpMethods.IsGet(method) || HttpMethods.IsPatch(method))
            && (path.Equals("/api/instance/settings/auth-provider", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/instance/settings/authz-provider", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        const string onboardingBasePath = "/api/InstanceOnboarding/";

        if (!path.StartsWith(onboardingBasePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var endpoint = path[onboardingBasePath.Length..].TrimEnd('/');
        return endpoint.Equals("status", StringComparison.OrdinalIgnoreCase)
            || endpoint.Equals("complete", StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsPost(method) && endpoint.Equals("complete-local", StringComparison.OrdinalIgnoreCase)
            || endpoint.Equals("validate-secret", StringComparison.OrdinalIgnoreCase)
            || MatchesEndpointFamily(endpoint, "auth-provider-configuration")
            || MatchesEndpointFamily(endpoint, "authz-provider-configuration");
    }

    private static bool MatchesEndpointFamily(string endpoint, string root) =>
        endpoint.Equals(root, StringComparison.OrdinalIgnoreCase)
        || endpoint.StartsWith($"{root}/", StringComparison.OrdinalIgnoreCase);
}
