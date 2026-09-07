using System.Text.RegularExpressions;
using Blazouter.Interfaces;
using Blazouter.Models;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Routing.Guards;

public sealed partial class GroupAdminRouteGuard(
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

        var groupId = ExtractGroupIdFromPath(match.MatchedPath);
        if (groupId == null)
        {
            return false;
        }

        var authority = await userService.GetAdminAuthorityAsync().ConfigureAwait(false);
        return authority?.AdminGroupIds?.Contains(groupId.Value) == true;
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

    private static Guid? ExtractGroupIdFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var match = GroupIdPattern().Match(path);
        if (!match.Success)
            return null;

        return Guid.TryParse(match.Groups[1].Value, out var groupId) ? groupId : null;
    }

    [GeneratedRegex(@"/group/([0-9a-fA-F\-]{36})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GroupIdPattern();
}
