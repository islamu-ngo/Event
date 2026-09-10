using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain.Enums;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.API.HealthChecks;

public sealed class SmtpHealthCheck(
    IEmailDeliveryCapabilityResolver capabilities,
    IEmailConnectionTester connectionTester) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        EmailDeliveryCapability capability;
        try
        {
            capability = await capabilities.ResolveAsync(null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failed policy read is a core authority failure, not an optional transport outage.
            return HealthCheckResult.Unhealthy("email_capability_unavailable");
        }

        var data = new Dictionary<string, object>
        {
            ["enabled"] = capability.Enabled,
            ["state"] = capability.State.ToString()
        };
        if (!capability.Enabled)
        {
            return HealthCheckResult.Healthy("smtp_disabled", data);
        }
        if (capability.State != EmailDeliveryState.Available)
        {
            return HealthCheckResult.Degraded("smtp_configuration_unavailable", data: data);
        }

        try
        {
            var result = await connectionTester.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
            data["durationMs"] = result.Duration.TotalMilliseconds;
            return result.Success
                ? HealthCheckResult.Healthy("smtp_available", data)
                : HealthCheckResult.Degraded("smtp_unavailable", data: data);
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("email_capability_unavailable", data: data);
        }
    }
}
