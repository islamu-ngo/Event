using System.Security.Claims;
using Event.Web.BffHosting.Security;
using Event.Web.BffHosting.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Explore.Blazor.Services.Auth;

namespace Explore.Blazor.Services;

internal sealed class ExploreBffAccessTokenProvider : IEventBffAccessTokenProvider
{
    public async ValueTask<string?> ResolveAccessTokenAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!BffSessionIdentity.TryGetBinding(httpContext.User, out var binding)
            || httpContext.RequestServices.GetService<IBffSessionAuthority>() is not { } authority)
        {
            return null;
        }

        try
        {
            if (!await authority.IsActiveAsync(binding, cancellationToken)) return null;
        }
        catch (BffSessionAuthorityUnavailableException)
        {
            return null;
        }

        var cookieToken = await httpContext.GetTokenAsync("access_token");
        var token = !string.IsNullOrEmpty(cookieToken) && CircuitTokenStore.IsTokenForwardable(cookieToken)
            ? cookieToken : TryResolveFromCircuitStore(httpContext, binding);
        if (!BffSessionIdentity.TryGetBinding(httpContext.User, out var currentBinding) || currentBinding != binding)
        {
            return null;
        }

        try
        {
            return await authority.IsActiveAsync(binding, cancellationToken) ? token : null;
        }
        catch (BffSessionAuthorityUnavailableException)
        {
            return null;
        }
    }

    private static string? TryResolveFromCircuitStore(HttpContext httpContext, BffSessionBinding binding)
    {
        var tokenStore = httpContext.RequestServices.GetService<ICircuitTokenStore>();
        if (tokenStore is null)
        {
            return null;
        }

        var resolution = tokenStore.Resolve(binding.OwnerPartitionKey, binding.Handle);
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
