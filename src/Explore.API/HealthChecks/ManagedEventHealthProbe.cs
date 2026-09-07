using Explore.Application.Contracts.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.API.HealthChecks;

public sealed class ManagedEventHealthProbe(HealthCheckService healthCheckService)
    : IManagedEventHealthProbe
{
    public async Task<ManagedEventHealthObservation> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("ready"),
            cancellationToken);
        return new ManagedEventHealthObservation(report.Status.ToString(), DateTime.UtcNow);
    }
}
