using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookDeliveryGovernanceResolverTests
{
    [Test]
    public async Task ResolveAsync_AppliesTenantSettingsWithoutExceedingStartupCeilings()
    {
        var tenantId = Guid.CreateVersion7();
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var governed = new WebhookDeliverySettingGroup();
        governed.Populate(new Dictionary<string, ResolvedSetting>
        {
            ["webhook.delivery.max_concurrent_deliveries"] = Value("webhook.delivery.max_concurrent_deliveries", 32),
            ["webhook.delivery.max_concurrent_deliveries_per_tenant"] = Value("webhook.delivery.max_concurrent_deliveries_per_tenant", 12),
            ["webhook.delivery.max_concurrent_deliveries_per_endpoint"] = Value("webhook.delivery.max_concurrent_deliveries_per_endpoint", 6),
            ["webhook.delivery.max_items_per_tenant_per_claim_cycle"] = Value("webhook.delivery.max_items_per_tenant_per_claim_cycle", 20),
            ["webhook.delivery.max_attempts"] = Value("webhook.delivery.max_attempts", 10),
            ["webhook.delivery.endpoint_timeout_seconds"] = Value("webhook.delivery.endpoint_timeout_seconds", 30),
            ["webhook.delivery.auto_pause_threshold"] = Value("webhook.delivery.auto_pause_threshold", 7)
        });
        settingsResolver.ResolveGroupAsync<WebhookDeliverySettingGroup>(
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(governed);
        var resolver = new WebhookDeliveryGovernanceResolver(
            settingsResolver,
            Options.Create(new WebhookDeliveryProcessorSettings
            {
                MaxConcurrentDeliveries = 16,
                MaxConcurrentDeliveriesPerTenant = 4,
                MaxConcurrentDeliveriesPerEndpoint = 2,
                MaxItemsPerTenantPerClaimCycle = 8
            }),
            new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions
            {
                Local = new WebhookLocalOptions
                {
                    MaxAttempts = 8,
                    TimeoutSeconds = 15
                }
            }));

        var policy = await resolver.ResolveAsync(tenantId, CancellationToken.None);

        await Assert.That(policy.GlobalInFlightLimit).IsEqualTo(16);
        await Assert.That(policy.MaxInFlightPerTenant).IsEqualTo(4);
        await Assert.That(policy.MaxInFlightPerEndpoint).IsEqualTo(2);
        await Assert.That(policy.MaxItemsPerTenantPerClaimCycle).IsEqualTo(8);
        await Assert.That(policy.MaxAttempts).IsEqualTo(8);
        await Assert.That(policy.EndpointTimeoutSeconds).IsEqualTo(15);
        await Assert.That(policy.AutoPauseThreshold).IsEqualTo(7);
        await Assert.That(policy.ResolutionVersion).IsEqualTo("webhook-delivery-v1:g16:t4:e2:c8:a8:o15:p7");
        await settingsResolver.Received(1).ResolveGroupAsync<WebhookDeliverySettingGroup>(
            Arg.Is<SettingContext>(context => context.TenantId == tenantId),
            Arg.Any<CancellationToken>());
    }

    private static ResolvedSetting Value(string key, int value) => new()
    {
        Key = key,
        Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
