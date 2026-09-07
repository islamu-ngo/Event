using Explore.Infrastructure.Services.Federation;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class AtprotoJetstreamReadinessHealthCheck(
    IAtprotoJetstreamRuntimeStore store,
    IOptions<AtprotoJetstreamOptions> options,
    AtprotoJetstreamLiveness liveness,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> enabledTenants;
        try
        {
            enabledTenants = await store.ResolveEnabledTenantIdsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "ATProto Jetstream capability readiness could not be resolved; ingestion remains stopped.");
        }

        AtprotoJetstreamLivenessSnapshot snapshot = liveness.Read();
        var data = new Dictionary<string, object>
        {
            ["capabilityEnabled"] = enabledTenants.Count > 0,
            ["allowlistConfigured"] = options.Value.AllowedDids is { Length: > 0 },
            ["connected"] = snapshot.IsConnected,
            ["cursor"] = snapshot.Cursor
        };
        if (enabledTenants.Count == 0)
        {
            return HealthCheckResult.Healthy(
                "ATProto Jetstream ingestion is dormant because no tenant capability is enabled.",
                data);
        }

        if (snapshot.IsConnected)
        {
            return options.Value.AllowedDids is { Length: > 0 }
                ? HealthCheckResult.Healthy(
                    "ATProto Jetstream capability and curated DID filter are ready and connected.",
                    data)
                : HealthCheckResult.Healthy(
                    "ATProto Jetstream capability and public collection subscription are ready and connected.",
                    data);
        }

        // A capability is enabled but no subscription is open. Reconnects are routine and bounded, so
        // this only degrades once the outage outlives the configured reconnect budget.
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        TimeSpan tolerance = TimeSpan.FromMilliseconds(
            Math.Max(options.Value.CapabilityPollMilliseconds, options.Value.RetryMaximumMilliseconds) * 2);
        TimeSpan? outage = snapshot.DisconnectedSince is { } since ? now - since : null;
        data["disconnectedForSeconds"] = outage?.TotalSeconds ?? 0d;
        if (outage is null || outage <= tolerance)
        {
            return HealthCheckResult.Healthy(
                "ATProto Jetstream ingestion is enabled and reconnecting within its bounded retry budget.",
                data);
        }

        return HealthCheckResult.Degraded(
            "ATProto Jetstream ingestion is enabled but has had no open subscription beyond its reconnect budget.",
            data: data);
    }
}
