using Explore.API.HealthChecks;
using Explore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class AiRetentionCleanupHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsyncWhenCleanupEnabledReturnsHealthyWithSafeData()
    {
        var options = Options.Create(new AiRetentionCleanupSettings
        {
            Enabled = true,
            DryRun = false,
            InitialDelaySeconds = 5,
            PollingIntervalMinutes = 15,
            MaxTenantsPerPass = 25
        });
        var healthCheck = new AiRetentionCleanupHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("redaction mode");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("dryRun").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("initialDelaySeconds").And.Value.IsEqualTo(5);
        await Assert.That(result.Data).ContainsKey("pollingIntervalMinutes").And.Value.IsEqualTo(15);
        await Assert.That(result.Data).ContainsKey("maxTenantsPerPass").And.Value.IsEqualTo(25);
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Equals("tenantId", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Equals("tenantSlug", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("payload", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task CheckHealthAsyncWhenCleanupDryRunReturnsHealthy()
    {
        var options = Options.Create(new AiRetentionCleanupSettings
        {
            Enabled = true,
            DryRun = true
        });
        var healthCheck = new AiRetentionCleanupHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("dry-run mode");
        await Assert.That(result.Data).ContainsKey("dryRun").And.Value.IsEqualTo(true);
    }

    [Test]
    public async Task CheckHealthAsyncWhenCleanupDisabledReturnsDegraded()
    {
        var options = Options.Create(new AiRetentionCleanupSettings
        {
            Enabled = false
        });
        var healthCheck = new AiRetentionCleanupHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("intentionally disabled");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(false);
    }
}
