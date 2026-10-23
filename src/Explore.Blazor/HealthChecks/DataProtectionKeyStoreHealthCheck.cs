using Explore.ServiceDefaults.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Blazor.HealthChecks;

public sealed class DataProtectionKeyStoreHealthCheck(ExternalDataProtectionReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        readiness.CheckHealthAsync(context, cancellationToken);
}
