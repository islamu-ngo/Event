using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
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
        var targetRepository = Substitute.For<IWebhookLocalDeliveryHealthObserver>();
        var healthCheck = CreateHealthCheck(
            targetRepository,
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderSvix },
            new WebhookDeliveryProcessorSettings());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["localProviderSelected"]).IsEqualTo(false);
        await targetRepository.DidNotReceiveWithAnyArgs().ObserveAsync(default, default);
    }

    [Test]
    public async Task CheckHealthAsync_WhenProcessorDisabled_ReturnsDegraded()
    {
        var healthCheck = CreateHealthCheck(
            Substitute.For<IWebhookLocalDeliveryHealthObserver>(),
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderLocal },
            new WebhookDeliveryProcessorSettings { Enabled = false });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["processorEnabled"]).IsEqualTo(false);
    }

    [Test]
    public async Task CheckHealthAsync_WhenQueueBelowThreshold_ReturnsHealthyWithSafeCounts()
    {
        var targetRepository = Substitute.For<IWebhookLocalDeliveryHealthObserver>();
        targetRepository.ObserveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookLocalDeliveryHealth(5, 0));
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
        var targetRepository = Substitute.For<IWebhookLocalDeliveryHealthObserver>();
        targetRepository.ObserveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookLocalDeliveryHealth(0, 1));
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
    public async Task CheckHealthAsync_WhenAuthorityFails_ReturnsUnhealthyWithoutAggregateData()
    {
        var observer = Substitute.For<IWebhookLocalDeliveryHealthObserver>();
        observer.ObserveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WebhookLocalDeliveryHealth>(
                new InvalidOperationException("webhook_health_authority_not_ready")));
        var healthCheck = CreateHealthCheck(observer,
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderLocal },
            new WebhookDeliveryProcessorSettings());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Data.Keys).DoesNotContain("dueLocalTargets");
        await Assert.That(result.Data.Keys).DoesNotContain("staleDeliveringTargets");
        await Assert.That(result.Data.Keys).DoesNotContain("observedAtUtc");
    }

    [Test]
    public async Task CheckHealthAsync_WhenObservationCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var observer = Substitute.For<IWebhookLocalDeliveryHealthObserver>();
        observer.ObserveAsync(Arg.Any<DateTimeOffset>(), cancellation.Token)
            .Returns(Task.FromCanceled<WebhookLocalDeliveryHealth>(cancellation.Token));
        var healthCheck = CreateHealthCheck(observer,
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderLocal },
            new WebhookDeliveryProcessorSettings());

        await Assert.That(async () => await healthCheck.CheckHealthAsync(
            new HealthCheckContext(), cancellation.Token)).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task CoopEffectHealthCheck_WhenBacklogAtThreshold_ReturnsDegradedWithSafeCounts()
    {
        await using var database = await EffectHealthDatabase.CreateAsync(10);
        var healthCheck = CreateEffectHealthCheck(database.Reader, new IncomingWebhookProcessingSettings
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
        var healthCheck = CreateEffectHealthCheck(null, new IncomingWebhookProcessingSettings
        {
            Enabled = false
        });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data.Keys).DoesNotContain("dueEffects");
        await Assert.That(result.Data.Keys).DoesNotContain("staleLeases");
    }

    private static LocalWebhookDeliveryHealthCheck CreateHealthCheck(
        IWebhookLocalDeliveryHealthObserver targetRepository,
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
        IIncomingWebhookEffectHealthReader? reader,
        IncomingWebhookProcessingSettings settings)
    {
        var services = new ServiceCollection();
        if (reader is not null)
            services.AddScoped<IIncomingWebhookEffectHealthReader>(_ => reader);
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

    private sealed class EffectHealthDatabase : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"effect-health-{Guid.NewGuid():N}.db");
        private readonly GlobalExploreDbContextFactory factory;

        private EffectHealthDatabase()
        {
            factory = new GlobalExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = path
            });
            Reader = new IncomingWebhookEffectHealthReader(factory);
        }

        internal IIncomingWebhookEffectHealthReader Reader { get; }

        internal static async Task<EffectHealthDatabase> CreateAsync(int dueEffects)
        {
            var database = new EffectHealthDatabase();
            try
            {
                await using var context = await database.factory.CreateDbContextAsync();
                await context.Database.EnsureCreatedAsync();
                context.Set<TenantStatus>().Add(new TenantStatus
                {
                    Id = (int)TenantStatusEnum.Active,
                    MasterCode = "active",
                    FullName = "Active",
                    IsActiveState = true
                });
                context.Set<IncomingWebhookMessageStatusLookup>().Add(new IncomingWebhookMessageStatusLookup
                {
                    Id = (int)IncomingWebhookMessageStatus.Verified,
                    MasterCode = "verified",
                    FullName = "Verified"
                });
                context.Set<WebhookPayloadProvenanceLookup>().Add(new WebhookPayloadProvenanceLookup
                {
                    Id = (int)WebhookPayloadProvenance.ExactBytes,
                    MasterCode = "exact_bytes",
                    FullName = "Exact bytes"
                });
                var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var tenant = new Tenant
                {
                    Id = Guid.CreateVersion7(),
                    FullName = "Effect health tenant",
                    Slug = "effect-health",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!,
                    CreatedAt = now
                };
                context.Tenants.Add(tenant);
                for (int index = 0; index < dueEffects; index++)
                {
                    byte[] payload = [1];
                    string hash = "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
                    var message = IncomingWebhookMessage.CreateVerified(
                        tenant.Id, "coop", $"health-{index}", null, null,
                        payload, hash, "application/json", "identity", null,
                        now, now, now.AddDays(1), "health-test",
                        now.AddDays(1), now.AddDays(1), now.AddDays(1), now.AddDays(1));
                    context.IncomingWebhookMessages.Add(message);
                    context.IncomingWebhookEffectOutboxes.Add(IncomingWebhookEffectOutbox.CreatePending(
                        tenant.Id, message.Id, "coop", $"decision-{index}", "health-effect", hash, now));
                }
                await context.SaveChangesAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
            return ValueTask.CompletedTask;
        }
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
