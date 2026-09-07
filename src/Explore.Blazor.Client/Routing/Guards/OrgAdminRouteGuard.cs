using System.Text.RegularExpressions;
using Blazouter.Interfaces;
using Blazouter.Models;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Routing.Guards;

public sealed partial class OrgAdminRouteGuard(
    AuthenticationStateProvider authStateProvider,
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

        var organizationId = ExtractOrganizationIdFromPath(match.MatchedPath);
        if (!organizationId.HasValue)
        {
            return false;
        }

        var authority = await userService.GetAdminAuthorityAsync().ConfigureAwait(false);
        return authority?.AdminOrganizationIds?.Contains(organizationId.Value) == true;
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

    private static Guid? ExtractOrganizationIdFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var match = OrgIdPattern().Match(path);
        return match.Success && Guid.TryParse(match.Groups[1].Value, out var organizationId)
            ? organizationId
            : null;
    }

    [GeneratedRegex(@"/organization/([0-9a-fA-F\-]{36})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OrgIdPattern();
}
