using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.ServiceDefaults.HealthChecks;

public sealed class DistributedCacheHealthCheck(
    IConfiguration configuration,
    IDistributedCache cache,
    IEnumerable<IDistributedCacheBackendState> backendStates) : IHealthCheck
{
    private const string ConnectionName = "cache";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var redisConfigured = !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionName));
        var states = backendStates.ToArray();
        var data = BuildData(redisConfigured, states);

        try
        {
            var key = $"health:distributed-cache:{Guid.CreateVersion7():N}";
            var expected = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

            await cache.SetStringAsync(
                key,
                expected,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                },
                cancellationToken).ConfigureAwait(false);

            var actual = await cache.GetStringAsync(key, cancellationToken).ConfigureAwait(false);
            await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                return HealthCheckResult.Unhealthy(
                    "Distributed cache round-trip returned an unexpected value.",
                    data: data);
            }
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                redisConfigured
                    ? "Configured distributed cache backend is not usable."
                    : "In-memory distributed cache is not usable.",
                ex,
                data);
        }

        if (states.Any(static state => state.IsConfigured && state.IsDegraded))
        {
            return HealthCheckResult.Degraded(
                "Distributed cache is usable, but a configured backend is degraded.",
                data: data);
        }

        return HealthCheckResult.Healthy(
            redisConfigured
                ? "Distributed cache is ready."
                : "Redis is not configured; using the in-memory distributed cache.",
            data);
    }

    private static Dictionary<string, object> BuildData(
        bool redisConfigured,
        IReadOnlyCollection<IDistributedCacheBackendState> states)
    {
        var data = new Dictionary<string, object>
        {
            ["redisConfigured"] = redisConfigured
        };

        foreach (var state in states)
        {
            data[$"backend:{state.BackendName}:configured"] = state.IsConfigured;
            data[$"backend:{state.BackendName}:degraded"] = state.IsDegraded;
            data[$"backend:{state.BackendName}:status"] = state.Status;
        }

        return data;
    }
}
