using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class SvixWebhookProviderBindingAuthorityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task SelfHostedConformanceProfile_ResolvesWithoutManagedSaasFallback()
    {
        var service = CreateService(SupportedOptions(), Substitute.For<ISvixWebhookClient>());

        var result = service.ResolveCurrentProfile();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Profile!.ProviderKind).IsEqualTo(WebhookProviderKind.Svix);
        await Assert.That(result.Profile.ProviderEnvironment)
            .IsEqualTo(SvixConformanceProfileRegistry.SelfHostedEnvironment);
        await Assert.That(result.Profile.CapabilityProfile.ProviderVersion)
            .IsEqualTo(SvixConformanceProfileRegistry.SelfHostedProviderVersion);
    }

    [Test]
    public async Task ManagedSaasProfile_IsRejectedBeforeProviderAccess()
    {
        var client = Substitute.For<ISvixWebhookClient>();
        var service = CreateService(new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new WebhookSvixOptions
            {
                BaseUrl = null,
                Environment = SvixConformanceProfileRegistry.ManagedEnvironment,
                ProviderVersion = SvixConformanceProfileRegistry.ManagedProviderVersion,
                CapabilityPolicyVersion = SvixConformanceProfileRegistry.ManagedCapabilityPolicyVersion
            }
        }, client);

        var result = service.ResolveCurrentProfile();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("svix_self_hosted_profile_unsupported");
        await client.DidNotReceiveWithAnyArgs().GetApplicationAsync(default!, default);
    }

    [Test]
    public async Task ExactUidAndOwnershipMetadata_VerifiesWhileConsumerSubstitutionFails()
    {
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var instanceId = Guid.CreateVersion7();
        var applicationUid = WebhookConsumerProviderBinding.CreateApplicationUid(instanceId, consumerId);
        var client = Substitute.For<ISvixWebhookClient>();
        client.GetApplicationAsync("app_verified", Arg.Any<CancellationToken>())
            .Returns(new SvixApplicationBindingResult(
                "app_verified",
                applicationUid,
                new Dictionary<string, string>
                {
                    ["islamu.tenant_id"] = tenantId.ToString("D"),
                    ["islamu.consumer_id"] = consumerId.ToString("D"),
                    ["islamu.owner_id"] = tenantId.ToString("D"),
                    ["islamu.owner_kind_id"] = ((int)WebhookConsumerKind.Tenant).ToString()
                }));
        var service = CreateService(SupportedOptions(), client);
        var request = new WebhookProviderBindingOwnershipRequest(
            WebhookOwnershipScope.Create(
                WebhookConsumerKind.Tenant,
                tenantId,
                null,
                null,
                null,
                null),
            consumerId,
            applicationUid,
            "app_verified",
            WebhookProviderKind.Svix,
            SvixConformanceProfileRegistry.SelfHostedEnvironment,
            SvixConformanceProfileRegistry.SelfHostedProviderVersion,
            SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion);

        var verified = await service.VerifyOwnershipAsync(request, CancellationToken.None);
        var substituted = await service.VerifyOwnershipAsync(
            request with { WebhookConsumerId = Guid.CreateVersion7() },
            CancellationToken.None);

        await Assert.That(verified.Succeeded).IsTrue();
        await Assert.That(substituted.Succeeded).IsFalse();
        await Assert.That(substituted.FailureCategory).IsEqualTo("webhook_provider_binding_mismatched");
    }

    private static SvixWebhookProviderBindingAuthorityService CreateService(
        WebhookOptions options,
        ISvixWebhookClient client) =>
        new(
            client,
            new StaticOptionsMonitor<WebhookOptions>(options),
            new FixedTimeProvider(Now));

    private static WebhookOptions SupportedOptions() => new()
    {
        Provider = WebhookOptions.ProviderSvix,
        Svix = new WebhookSvixOptions
        {
            BaseUrl = "http://svix:8071",
            Environment = SvixConformanceProfileRegistry.SelfHostedEnvironment,
            ProviderVersion = SvixConformanceProfileRegistry.SelfHostedProviderVersion,
            CapabilityPolicyVersion = SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion
        }
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
