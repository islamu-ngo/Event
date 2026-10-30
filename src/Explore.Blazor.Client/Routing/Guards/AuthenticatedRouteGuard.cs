using Blazouter.Interfaces;
using Blazouter.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Routing.Guards;

public sealed class AuthenticatedRouteGuard(AuthenticationStateProvider authStateProvider) : IRouteGuard
{
    public async Task<bool> CanActivateAsync(RouteMatch match)
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        return authState.User.Identity?.IsAuthenticated ?? false;
    }

    public Task<string?> GetRedirectPathAsync(RouteMatch match)
    {
        var returnUrl = string.IsNullOrWhiteSpace(match.MatchedPath)
            ? "/"
            : match.MatchedPath;

        return Task.FromResult<string?>($"login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }
}
