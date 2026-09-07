using Event.Api.IntegrationTests.Fixtures;
using Explore.API.HealthChecks;
using Explore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Email)]
public sealed class EmailDispatchRetentionCleanupHealthCheckTests
{
    [Test]
    public async Task EnabledDryRunReportsHealthyWithBoundedSettings()
    {
        var check = new EmailDispatchRetentionCleanupHealthCheck(Options.Create(new EmailDispatchRetentionSettings
        {
            Enabled = true,
            DryRun = true,
            RetentionDays = 180,
            BatchSize = 500,
            MaxTenantsPerPass = 100
        }));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("dry-run");
        await Assert.That(result.Data["retentionDays"]).IsEqualTo(180);
        await Assert.That(result.Data["batchSize"]).IsEqualTo(500);
        await Assert.That(result.Data["maxTenantsPerPass"]).IsEqualTo(100);
    }

    [Test]
    public async Task DisabledCleanupReportsDegraded()
    {
        var check = new EmailDispatchRetentionCleanupHealthCheck(Options.Create(new EmailDispatchRetentionSettings
        {
            Enabled = false
        }));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("intentionally disabled");
    }
}
