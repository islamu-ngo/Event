using Explore.Blazor.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Explore.Blazor.HealthChecks;

public sealed class DataProtectionKeyStoreHealthCheck(
    IEnumerable<IConnectionMultiplexer> connectionMultiplexers,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<DataProtectionKeyStoreHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionMultiplexer = connectionMultiplexers.FirstOrDefault();
        var store = connectionMultiplexer is null ? "local" : "redis";

        try
        {
            if (connectionMultiplexer is null)
            {
                const string payload = "data-protection-health-check";
                var protector = dataProtectionProvider.CreateProtector(nameof(DataProtectionKeyStoreHealthCheck));
                var roundTrip = protector.Unprotect(protector.Protect(payload));

                return string.Equals(payload, roundTrip, StringComparison.Ordinal)
                    ? HealthCheckResult.Healthy(
                        "Local Data Protection key store is usable.",
                        new Dictionary<string, object> { ["store"] = store })
                    : HealthCheckResult.Unhealthy(
                        "Local Data Protection key store round-trip failed.",
                        data: new Dictionary<string, object> { ["store"] = store });
            }

            var database = connectionMultiplexer.GetDatabase();
            var latency = await database.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            var keyRingPresent = await database
                .KeyExistsAsync(BffDataProtectionExtensions.KeyRingName)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy(
                "Data Protection key store is reachable.",
                new Dictionary<string, object>
                {
                    ["keyRingPresent"] = keyRingPresent,
                    ["latencyMilliseconds"] = latency.TotalMilliseconds,
                    ["store"] = store
                });
        }
        catch (Exception ex)
        {
            var failureType = ex.GetType().Name;
            logger.LogWarning("Data Protection key store health check failed with {FailureType}.", failureType);

            return HealthCheckResult.Unhealthy(
                store == "redis"
                    ? "Data Protection key store is unreachable."
                    : "Local Data Protection key store is unusable.",
                ex,
                new Dictionary<string, object>
                {
                    ["failureType"] = failureType,
                    ["store"] = store
                });
        }
    }
}
