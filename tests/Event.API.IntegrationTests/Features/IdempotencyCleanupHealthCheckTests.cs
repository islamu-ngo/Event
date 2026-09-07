using Explore.API.HealthChecks;
using Explore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class IdempotencyCleanupHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsyncWhenCleanupEnabledReturnsHealthyWithSafeData()
    {
        var options = Options.Create(new IdempotencyCleanupSettings
        {
            Enabled = true,
            DryRun = false,
            InitialDelaySeconds = 5,
            PollingIntervalMinutes = 15,
            BatchSize = 25,
            ExpirationGraceHours = 12
        });
        var healthCheck = new IdempotencyCleanupHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("delete mode");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("dryRun").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("initialDelaySeconds").And.Value.IsEqualTo(5);
        await Assert.That(result.Data).ContainsKey("pollingIntervalMinutes").And.Value.IsEqualTo(15);
        await Assert.That(result.Data).ContainsKey("batchSize").And.Value.IsEqualTo(25);
        await Assert.That(result.Data).ContainsKey("expirationGraceHours").And.Value.IsEqualTo(12);
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("key", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("path", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task CheckHealthAsyncWhenCleanupDryRunReturnsHealthy()
    {
        var options = Options.Create(new IdempotencyCleanupSettings
        {
            Enabled = true,
            DryRun = true
        });
        var healthCheck = new IdempotencyCleanupHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("dry-run mode");
        await Assert.That(result.Data).ContainsKey("dryRun").And.Value.IsEqualTo(true);
    }

    [Test]
    public async Task CheckHealthAsyncWhenCleanupDisabledReturnsDegraded()
    {
        var options = Options.Create(new IdempotencyCleanupSettings
        {
            Enabled = false
        });
        var healthCheck = new IdempotencyCleanupHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("intentionally disabled");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(false);
    }
}
