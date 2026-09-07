using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Blazor.HealthChecks;

public sealed class ApiReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var probe = scope.ServiceProvider.GetRequiredService<IExploreApiReadinessProbe>();
            await probe.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Explore API generated-client probe succeeded.");
        }
        catch (Explore.Blazor.Client.Clients.ApiException ex)
        {
            return HealthCheckResult.Unhealthy(
                "Explore API generated-client probe returned a non-success status code.",
                data: new Dictionary<string, object> { ["statusCode"] = ex.StatusCode });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Explore API generated-client probe is unreachable.", ex);
        }
    }
}
