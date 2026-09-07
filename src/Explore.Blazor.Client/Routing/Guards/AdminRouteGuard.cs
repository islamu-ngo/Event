using Blazouter.Interfaces;
using Blazouter.Models;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Routing.Guards;

/// <summary>
/// Guards instance-admin routes by verifying the BFF-reported platform-scoped instance admin authority.
/// </summary>
public sealed class AdminRouteGuard(
    AuthenticationStateProvider authStateProvider,
    IInstanceOnboardingService instanceOnboardingService,
    IUserService userService) : IRouteGuard
{
    public async Task<bool> CanActivateAsync(RouteMatch match)
    {
        if (authStateProvider is null)
        {
            return false;
        }

        var authState = await authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        var user = authState.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var authority = await userService.GetAdminAuthorityAsync().ConfigureAwait(false);
        if (authority?.IsInstanceAdmin == true)
        {
            return true;
        }

        var instanceStatus = await instanceOnboardingService.GetStatusAsync().ConfigureAwait(false);
        return instanceStatus?.IsAuthenticated == true && instanceStatus.IsCurrentUserInstanceAdmin == true;
    }

    public async Task<string?> GetRedirectPathAsync(RouteMatch match)
    {
        if (authStateProvider is not null)
        {
            var authState = await authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            if (authState?.User?.Identity?.IsAuthenticated == true)
            {
                return "/";
            }
        }

        var returnUrl = string.IsNullOrWhiteSpace(match.MatchedPath)
            ? "/"
            : match.MatchedPath;

        return $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}
