using Blazouter.Interfaces;
using Blazouter.Models;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Routing.Guards;

public sealed class TenantAdminRouteGuard(
    AuthenticationStateProvider authStateProvider,
    ITenantOnboardingService tenantOnboardingService,
    IUserService userService) : IRouteGuard
{
    public async Task<bool> CanActivateAsync(RouteMatch match)
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        var user = authState.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var authority = await userService.GetAdminAuthorityAsync().ConfigureAwait(false);
        if (authority?.AdminTenantIds?.Any() == true)
        {
            return true;
        }

        var tenantStatus = await tenantOnboardingService.GetStatusAsync().ConfigureAwait(false);
        return tenantStatus?.IsAuthenticated == true && tenantStatus.IsCurrentUserTenantAdministrator == true;
    }

    public async Task<string?> GetRedirectPathAsync(RouteMatch match)
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        if (authState.User.Identity?.IsAuthenticated == true)
        {
            return "/";
        }

        var returnUrl = string.IsNullOrWhiteSpace(match.MatchedPath)
            ? "/"
            : match.MatchedPath;

        return $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

}
