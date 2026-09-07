using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class SvixPortalCapabilityCeilingTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid ConsumerId = Guid.Parse("018f0000-0000-7000-8000-000000000050");

    [Test]
    public async Task EffectiveCapability_IsIntersectionOfAuthorizationBindingProviderAndGovernance()
    {
        var authorization = typeof(OpenSvixAppPortalCommand)
            .GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true)
            .Cast<AuthorizeResourceAttribute>()
            .Single();
        var fixture = CreateFixture(
            WebhookProviderCapability.AppPortal |
            WebhookProviderCapability.EndpointManagement |
            WebhookProviderCapability.Replay,
            WebhookProviderCapability.AppPortal |
            WebhookProviderCapability.EndpointManagement);

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(ConsumerId, "session-1", null),
            CancellationToken.None);

        await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Webhooks.OpenProviderPortal);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(fixture.PortalRequest).IsNotNull();
        await Assert.That(fixture.PortalRequest!.ReadOnly).IsFalse();
        await Assert.That(fixture.PortalRequest.FeatureFlags).IsEquivalentTo(["ViewBase", "ManageEndpoint"]);
    }

    [Test]
    public async Task MissingVerifiedBinding_RemovesPortalAccess()
    {
        var fixture = CreateFixture(
            WebhookProviderCapability.AppPortal,
            WebhookProviderCapability.AppPortal,
            persistBinding: false);

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(ConsumerId, "session-1", null),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("webhook_provider_binding_unverified");
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .CreateAppPortalAccessAsync(default!, default);
    }

    [Test]
    public async Task ProviderProfileWithoutAppPortal_RemovesPortalAccess()
    {
        var fixture = CreateFixture(
            WebhookProviderCapability.EndpointManagement,
            WebhookProviderCapability.AppPortal | WebhookProviderCapability.EndpointManagement);

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(ConsumerId, "session-1", null),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("webhook_provider_capability_unavailable");
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .CreateAppPortalAccessAsync(default!, default);
    }

    [Test]
    public async Task GovernanceWithoutAppPortal_RemovesPortalAccess()
    {
        var fixture = CreateFixture(
            WebhookProviderCapability.AppPortal | WebhookProviderCapability.EndpointManagement,
            WebhookProviderCapability.EndpointManagement);

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(ConsumerId, "session-1", null),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("webhook_provider_capability_unavailable");
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .CreateAppPortalAccessAsync(default!, default);
    }

    [Test]
    public async Task UnsupportedProviderVersion_RemovesPortalAccess()
    {
        var fixture = CreateFixture(
            WebhookProviderCapability.AppPortal,
            WebhookProviderCapability.AppPortal,
            providerVersion: "unknown");

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(ConsumerId, "session-1", null),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("webhook_provider_capability_unavailable");
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .CreateAppPortalAccessAsync(default!, default);
    }

    [Test]
    public async Task StaleCapabilityPolicyVersion_RemovesPortalAccess()
    {
        var fixture = CreateFixture(
            WebhookProviderCapability.AppPortal,
            WebhookProviderCapability.AppPortal,
            capabilityPolicyVersion: "svix-stale-v1");

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(ConsumerId, "session-1", null),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("webhook_provider_capability_unavailable");
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .CreateAppPortalAccessAsync(default!, default);
    }

    private static Fixture CreateFixture(
        WebhookProviderCapability providerCapabilities,
        WebhookProviderCapability governanceCapabilities,
        bool persistBinding = true,
        string providerVersion = "1.96.1",
        string capabilityPolicyVersion = "svix-1.96.1-v1")
    {
        var svixClient = Substitute.For<ISvixWebhookClient>();
        var consumerRepository = Substitute.For<IWebhookConsumerRepository>();
        var bindingRepository = Substitute.For<IWebhookConsumerProviderBindingRepository>();
        var options = new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new WebhookSvixOptions
            {
                Environment = "production",
                ProviderVersion = "1.96.1",
                CapabilityPolicyVersion = "svix-1.96.1-v1"
            }
        });
        var consumer = new WebhookConsumer
        {
            Id = ConsumerId,
            TenantId = TenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "Capability ceiling consumer",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Svix
        };
        var profile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            providerVersion,
            providerCapabilities,
            capabilityPolicyVersion,
            DateTimeOffset.UtcNow);
        var binding = WebhookConsumerProviderBinding.CreatePending(
            TenantId,
            ConsumerId,
            Guid.CreateVersion7(),
            "production",
            profile,
            governanceCapabilities);
        binding.VerifyOwnership(TenantId, ConsumerId, "app_capability_ceiling", DateTimeOffset.UtcNow);

        consumerRepository.GetByIdForOwnerOperationAsync(
                ConsumerId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(consumer);
        if (persistBinding)
        {
            bindingRepository.GetVerifiedByConsumerAsync(
                    TenantId,
                    ConsumerId,
                    WebhookProviderKind.Svix,
                    "production",
                    Arg.Any<CancellationToken>())
                .Returns(binding);
        }

        svixClient.GetApplicationAsync("app_capability_ceiling", Arg.Any<CancellationToken>())
            .Returns(new SvixApplicationBindingResult(
                "app_capability_ceiling",
                binding.ApplicationUid,
                new Dictionary<string, string>
                {
                    ["islamu.tenant_id"] = TenantId.ToString("D"),
                    ["islamu.consumer_id"] = ConsumerId.ToString("D"),
                    ["islamu.owner_id"] = TenantId.ToString("D"),
                    ["islamu.owner_kind_id"] = ((int)WebhookConsumerKind.Tenant).ToString()
                }));
        SvixAppPortalAccessRequest? portalRequest = null;
        svixClient.CreateAppPortalAccessAsync(
                Arg.Do<SvixAppPortalAccessRequest>(request => portalRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(new SvixAppPortalAccessResult("https://svix.example/app-portal", "portal-token"));

        return new Fixture(
            new SvixAppPortalService(svixClient, consumerRepository, bindingRepository, options),
            svixClient,
            () => portalRequest);
    }

    private sealed class Fixture(
        SvixAppPortalService service,
        ISvixWebhookClient svixClient,
        Func<SvixAppPortalAccessRequest?> portalRequest)
    {
        public SvixAppPortalService Service { get; } = service;
        public ISvixWebhookClient SvixClient { get; } = svixClient;
        public SvixAppPortalAccessRequest? PortalRequest => portalRequest();
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
