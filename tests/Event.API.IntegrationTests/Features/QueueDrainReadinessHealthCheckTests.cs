using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Infrastructure;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ApiIntegrationTests.Features;

public sealed class QueueDrainReadinessHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenCountsAreBelowThreshold_ReturnsTenantFreeHealthyData()
    {
        var repository = Substitute.For<IQueueDrainHealthRepository>();
        repository.GetSnapshotAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new QueueDrainHealthSnapshot(1, 0, 0, 2, 0, 3, 0, 4, 0, 0, 5, 0, 0));
        QueueDrainReadinessHealthCheck healthCheck = Create(repository);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["integrationDue"]).IsEqualTo(1);
        await Assert.That(result.Data["pdsDue"]).IsEqualTo(5);
        await Assert.That(result.Data.Keys).DoesNotContain("tenantId");
        await Assert.That(result.Data.Keys).DoesNotContain("payload");
        await Assert.That(result.Data.Keys).DoesNotContain("did");
        await Assert.That(result.Data.Keys).DoesNotContain("providerEventId");
    }

    [Test]
    public async Task CheckHealthAsync_WhenAnyLaneHasStaleOrAmbiguousWork_ReturnsDegraded()
    {
        var repository = Substitute.For<IQueueDrainHealthRepository>();
        repository.GetSnapshotAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new QueueDrainHealthSnapshot(0, 0, 1, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1));
        QueueDrainReadinessHealthCheck healthCheck = Create(repository);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["integrationAmbiguous"]).IsEqualTo(1);
        await Assert.That(result.Data["providerPublicationUnknown"]).IsEqualTo(1);
        await Assert.That(result.Data["pdsDeadLettered"]).IsEqualTo(1);
    }

    private static QueueDrainReadinessHealthCheck Create(IQueueDrainHealthRepository repository)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => repository);
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new QueueDrainReadinessHealthCheck(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IntegrationSyncProcessorSettings()),
            Options.Create(new IncomingWebhookProcessingSettings()),
            Options.Create(new WebhookBulkReplaySettings()),
            Options.Create(new WebhookProviderPublicationProcessorSettings { Enabled = true }),
            Options.Create(new PdsSyncSettings()),
            new BusinessMetrics(meterFactory),
            TimeProvider.System);
    }
}
