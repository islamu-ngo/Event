// Integrates with ASP.NET Core health check system for /health endpoints.

using Explore.Secrets.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Explore.Secrets.Observability;

/// <summary>
/// Health check that reports the status of the secret provider.
/// Registers with ASP.NET Core health checks for /health endpoint integration.
/// </summary>
public sealed class SecretProviderHealthCheck : IHealthCheck
{
    /// <summary>
    /// Health check name for registration.
    /// </summary>
    public const string Name = "secret_provider";

    /// <summary>
    /// Tag for categorizing this health check.
    /// </summary>
    public const string Tag = "secrets";

    private readonly ISecretProvider _provider;
    private readonly SecretRefreshMetrics? _metrics;
    private readonly ILogger<SecretProviderHealthCheck> _logger;
    private readonly TimeProvider _clock;

    public SecretProviderHealthCheck(
        ISecretProvider provider,
        ILogger<SecretProviderHealthCheck> logger,
        SecretRefreshMetrics? metrics = null,
        TimeProvider? clock = null)
    {
        _provider = provider;
        _logger = logger;
        _metrics = metrics;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthInfo = await _provider.GetHealthAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["provider"] = healthInfo.ProviderType.ToString(),
                ["supportsRefresh"] = _provider.SupportsRefresh,
                ["consecutiveFailures"] = healthInfo.ConsecutiveFailures
            };

            if (healthInfo.LastSuccessfulRefresh.HasValue)
            {
                data["lastSuccessfulRefresh"] = healthInfo.LastSuccessfulRefresh.Value.ToString("O");
                data["secondsSinceLastRefresh"] =
                    (_clock.GetUtcNow() - healthInfo.LastSuccessfulRefresh.Value)
                    .TotalSeconds;
            }

            // Add metrics data if available
            if (_metrics is not null)
            {
                data["metricsLastRefresh"] = _metrics.LastSuccessfulRefresh.ToString("O");
                data["metricsConsecutiveFailures"] = _metrics.ConsecutiveFailures;
            }

            if (healthInfo.IsHealthy)
            {
                return HealthCheckResult.Healthy(
                    description: $"Secret provider '{healthInfo.ProviderType}' is healthy",
                    data: data);
            }

            // Determine severity based on consecutive failures
            if (healthInfo.ConsecutiveFailures >= 3)
            {
                _logger.LogWarning(
                    "Secret provider unhealthy: {ProviderType} has {FailureCount} consecutive failures.",
                    healthInfo.ProviderType,
                    healthInfo.ConsecutiveFailures);

                return HealthCheckResult.Unhealthy(
                    description: $"Secret provider '{healthInfo.ProviderType}' is unavailable after {healthInfo.ConsecutiveFailures} consecutive failures.",
                    data: data);
            }

            return HealthCheckResult.Degraded(
                description: $"Secret provider '{healthInfo.ProviderType}' has transient resolution failures.",
                data: data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Health check failed for secret provider with {ExceptionType}.", ex.GetType().Name);

            return HealthCheckResult.Unhealthy(
                description: "Secret provider health check failed.",
                data: new Dictionary<string, object>
                {
                    ["provider"] = _provider.ProviderType.ToString(),
                    ["exceptionType"] = ex.GetType().Name
                });
        }
    }
}
