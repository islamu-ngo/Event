using Explore.Infrastructure.Geocoding;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.API.HealthChecks;

public sealed class GeocodingReadinessHealthCheck(
    GeocodingReadinessProbe probe)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        probe.CheckHealthAsync(context, cancellationToken);
}
