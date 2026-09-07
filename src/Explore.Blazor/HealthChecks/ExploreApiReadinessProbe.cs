using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.HealthChecks;

public interface IExploreApiReadinessProbe
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
}

public sealed class ExploreApiReadinessProbe(IInstanceMessagingSettingsClient apiClient) : IExploreApiReadinessProbe
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        _ = await apiClient.GetInstanceResolverConfigurationAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
