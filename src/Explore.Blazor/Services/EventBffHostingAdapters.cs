using System.Security.Claims;
using Event.Web.BffHosting.Security;
using Event.Web.BffHosting.Abstractions;
using Microsoft.AspNetCore.Authentication;

namespace Explore.Blazor.Services;

internal sealed class ExploreBffAccessTokenProvider : IEventBffAccessTokenProvider
{
    public async ValueTask<string?> ResolveAccessTokenAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var cookieToken = await httpContext.GetTokenAsync("access_token");
        if (!string.IsNullOrEmpty(cookieToken) && CircuitTokenStore.IsTokenForwardable(cookieToken))
        {
            return cookieToken;
        }

        return TryResolveFromCircuitStore(httpContext);
    }

    private static string? TryResolveFromCircuitStore(HttpContext httpContext)
    {
        var user = httpContext.User;
        var userId = user.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;

        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var sessionId = user.TryGetSessionId(out var resolvedSessionId) ? resolvedSessionId.PartitionKey : null;
        var tokenStore = httpContext.RequestServices.GetService<ICircuitTokenStore>();
        if (tokenStore is null)
        {
            return null;
        }

        var resolution = tokenStore.Resolve(userId, sessionId);
        if (!resolution.Found || string.IsNullOrEmpty(resolution.Token))
        {
            return null;
        }

        return CircuitTokenStore.IsTokenForwardable(resolution.Token)
            ? resolution.Token
            : null;
    }
}

internal sealed class ExploreBffTenantHintProvider(
    ITenantRouteContextAccessor tenantRouteContextAccessor) : IEventBffTenantHintProvider
{
    public string? ResolveTenantSlug(HttpContext httpContext) => tenantRouteContextAccessor.TenantSlug;
}

internal sealed class ExploreBffSetupSecretProvider(
    ISetupSecretResolver setupSecretResolver) : IEventBffSetupSecretProvider
{
    public ValueTask<string?> ResolveSetupSecretAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var resolution = setupSecretResolver.Resolve(httpContext);
        return ValueTask.FromResult(
            resolution.Found && !string.IsNullOrWhiteSpace(resolution.Secret)
                ? resolution.Secret
                : null);
    }
}

internal sealed class ExploreBffSupportAccessProvider(
    IBffSupportAccessSessionStore supportAccessSessionStore) : IEventBffSupportAccessProvider
{
    public async ValueTask<string?> ResolveSupportAccessSessionIdAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var resolution = await supportAccessSessionStore.ResolveCurrentAsync(cancellationToken);
        return resolution.Success && resolution.Session is not null
            ? resolution.Session.SessionId.ToString("D")
            : null;
    }
}
