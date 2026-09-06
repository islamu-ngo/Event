// ABOUTME: Reports bounded operational AT Protocol readiness while keeping optional providers and liveness independent.
// ABOUTME: Treats disabled login as healthy dormancy and never probes a user PDS or exposes configuration values.

using Explore.Blazor.Constants;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Blazor.HealthChecks;

public sealed class AtprotoAuthenticationHealthCheck(
    IDynamicAuthSchemeManager schemeManager,
    IBffProviderReadinessService readinessService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var registered = await schemeManager.GetRegisteredProviderSchemesAsync();
        if (!registered.Contains(AuthSchemeNames.Atproto, StringComparer.Ordinal))
        {
            return HealthCheckResult.Healthy(
                "AT Protocol login is disabled.",
                new Dictionary<string, object> { ["enabled"] = false });
        }

        var readiness = await readinessService.GetProviderReadinessAsync(
            AuthSchemeNames.Atproto,
            cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["enabled"] = true,
            ["failureCode"] = AtprotoAuthenticationMetrics.NormalizeFailureCode(readiness.FailureCode)
        };

        if (readiness.IsReady)
            return HealthCheckResult.Healthy("AT Protocol login prerequisites are ready.", data);
        return schemeManager.GetActivePrimaryProvider() is "local" or "keycloak"
            ? HealthCheckResult.Degraded("Optional AT Protocol login is unavailable.", data: data)
            : HealthCheckResult.Unhealthy("AT Protocol login is enabled but unavailable.", data: data);
    }
}
