using Explore.Application.Contracts.Persistence;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.WebPush;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.WebPush;

public sealed class WebPushDispatchHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenEnabledAndBelowThreshold_ReturnsHealthyWithSafeData()
    {
        var setup = CreateHealthCheck(dueDispatchCount: 1, retryScheduledCount: 1);

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["enabled"]).IsEqualTo(true);
        await Assert.That(result.Data["dueDispatchCount"]).IsEqualTo(1);
        await Assert.That(result.Data["retryScheduledCount"]).IsEqualTo(1);
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("payload", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("private", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("auth", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task CheckHealthAsync_WhenDisabled_ReturnsDegradedWithoutRepositoryCounts()
    {
        var setup = CreateHealthCheck(settings: new WebPushSettings
        {
            Enabled = false,
            VapidSubject = "mailto:ops@example.test",
            VapidPublicKey = "public-key",
            VapidPrivateKey = "private-key"
        });

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountDueDispatchAsync(default, default);
    }

    [Test]
    public async Task CheckHealthAsync_WhenStaleProcessingAtThreshold_ReturnsDegraded()
    {
        var setup = CreateHealthCheck(staleProcessingCount: 1);

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("stale processing");
    }

    [Test]
    public async Task CheckHealthAsync_WhenTerminalFailureAtThreshold_ReturnsDegraded()
    {
        var setup = CreateHealthCheck(terminalFailureCount: 1);

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("terminal");
    }

    private static (WebPushDispatchHealthCheck HealthCheck, IWebPushDispatchOutboxRepository Repository) CreateHealthCheck(
        WebPushSettings? settings = null,
        int dueDispatchCount = 0,
        int retryScheduledCount = 0,
        int staleProcessingCount = 0,
        int terminalFailureCount = 0)
    {
        var repository = Substitute.For<IWebPushDispatchOutboxRepository>();
        repository.CountDueDispatchAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(dueDispatchCount);
        repository.CountRetryScheduledAsync(Arg.Any<CancellationToken>()).Returns(retryScheduledCount);
        repository.CountStaleProcessingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(staleProcessingCount);
        repository.CountTerminalFailureAsync(Arg.Any<CancellationToken>()).Returns(terminalFailureCount);

        var services = new ServiceCollection().AddSingleton(repository).BuildServiceProvider();
        return (new WebPushDispatchHealthCheck(
            Options.Create(settings ?? new WebPushSettings
            {
                VapidSubject = "mailto:ops@example.test",
                VapidPublicKey = "public-key",
                VapidPrivateKey = "private-key",
                Enabled = true,
                HealthDueDispatchWarningThreshold = 10,
                HealthStaleProcessingWarningThreshold = 1,
                HealthTerminalFailureWarningThreshold = 1
            }),
            services.GetRequiredService<IServiceScopeFactory>()), repository);
    }
}
