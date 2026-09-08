using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class LocalWebhookDeliveryHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenLocalProviderNotSelected_ReturnsHealthyWithoutQueueQuery()
    {
        var targetRepository = Substitute.For<IWebhookLocalTargetRepository>();
        var healthCheck = CreateHealthCheck(
            targetRepository,
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderSvix },
            new WebhookDeliveryProcessorSettings());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["localProviderSelected"]).IsEqualTo(false);
        await targetRepository.DidNotReceiveWithAnyArgs().CountDueAsync(default, default);
        await targetRepository.DidNotReceiveWithAnyArgs().CountStaleDeliveringAsync(default, default);
    }

    [Test]
    public async Task CheckHealthAsync_WhenProcessorDisabled_ReturnsDegraded()
    {
        var healthCheck = CreateHealthCheck(
            Substitute.For<IWebhookLocalTargetRepository>(),
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderLocal },
            new WebhookDeliveryProcessorSettings { Enabled = false });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["processorEnabled"]).IsEqualTo(false);
    }

    [Test]
    public async Task CheckHealthAsync_WhenQueueBelowThreshold_ReturnsHealthyWithSafeCounts()
    {
        var targetRepository = Substitute.For<IWebhookLocalTargetRepository>();
        targetRepository.CountDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(5);
        targetRepository.CountStaleDeliveringAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var healthCheck = CreateHealthCheck(
            targetRepository,
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderLocal },
            new WebhookDeliveryProcessorSettings
            {
                HealthDueAttemptWarningThreshold = 10,
                HealthStaleSendingWarningThreshold = 1
            });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["dueLocalTargets"]).IsEqualTo(5);
        await Assert.That(result.Data["staleDeliveringTargets"]).IsEqualTo(0);
        await Assert.That(result.Data.Keys).DoesNotContain("endpointUrl");
        await Assert.That(result.Data.Keys).DoesNotContain("payloadJson");
        await Assert.That(result.Data.Keys).DoesNotContain("secretRef");
    }

    [Test]
    public async Task CheckHealthAsync_WhenStaleSendingAtThreshold_ReturnsDegraded()
    {
        var targetRepository = Substitute.For<IWebhookLocalTargetRepository>();
        targetRepository.CountDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(0);
        targetRepository.CountStaleDeliveringAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var healthCheck = CreateHealthCheck(
            targetRepository,
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderLocal },
            new WebhookDeliveryProcessorSettings
            {
                HealthDueAttemptWarningThreshold = 10,
                HealthStaleSendingWarningThreshold = 1
            });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["staleDeliveringTargets"]).IsEqualTo(1);
    }

    [Test]
    public async Task CoopEffectHealthCheck_WhenBacklogAtThreshold_ReturnsDegradedWithSafeCounts()
    {
        var repository = Substitute.For<IIncomingWebhookEffectOutboxRepository>();
        repository.CountDueAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(10);
        repository.CountStaleAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(0);
        var healthCheck = CreateEffectHealthCheck(repository, new IncomingWebhookProcessingSettings
        {
            EffectBacklogWarningThreshold = 10,
            EffectStaleLeaseWarningThreshold = 1
        });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["dueEffects"]).IsEqualTo(10);
        await Assert.That(result.Data["staleLeases"]).IsEqualTo(0);
        await Assert.That(result.Data.Keys).DoesNotContain("tenantId");
        await Assert.That(result.Data.Keys).DoesNotContain("providerDecisionId");
        await Assert.That(result.Data.Keys).DoesNotContain("payload");
    }

    [Test]
    public async Task CoopEffectHealthCheck_WhenDisabled_ReturnsDegradedWithoutQueueQuery()
    {
        var repository = Substitute.For<IIncomingWebhookEffectOutboxRepository>();
        var healthCheck = CreateEffectHealthCheck(repository, new IncomingWebhookProcessingSettings
        {
            Enabled = false
        });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await repository.DidNotReceiveWithAnyArgs().CountDueAsync(default, default);
        await repository.DidNotReceiveWithAnyArgs().CountStaleAsync(default, default);
    }

    private static LocalWebhookDeliveryHealthCheck CreateHealthCheck(
        IWebhookLocalTargetRepository targetRepository,
        WebhookOptions webhookOptions,
        WebhookDeliveryProcessorSettings settings)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => targetRepository);
        var serviceProvider = services.BuildServiceProvider();
        return new LocalWebhookDeliveryHealthCheck(
            Options.Create(settings),
            new StaticOptionsMonitor<WebhookOptions>(webhookOptions),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            CreateMetrics());
    }

    private static IncomingWebhookEffectHealthCheck CreateEffectHealthCheck(
        IIncomingWebhookEffectOutboxRepository repository,
        IncomingWebhookProcessingSettings settings)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => repository);
        var serviceProvider = services.BuildServiceProvider();
        return new IncomingWebhookEffectHealthCheck(
            Options.Create(settings),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            CreateMetrics(),
            TimeProvider.System);
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
