using Blazouter.Interfaces;
using Blazouter.Models;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Routing.Guards;

public sealed class MultiTenantControlPlaneRouteGuard(IInstanceOnboardingService instanceOnboardingService) : IRouteGuard
{
    public async Task<bool> CanActivateAsync(RouteMatch match)
    {
        var status = await instanceOnboardingService.GetSystemOnboardingStatusAsync().ConfigureAwait(false);
        return string.Equals(status?.DeploymentMode, "MultiTenant", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string?> GetRedirectPathAsync(RouteMatch match) => Task.FromResult<string?>("/settings/admin");
}
