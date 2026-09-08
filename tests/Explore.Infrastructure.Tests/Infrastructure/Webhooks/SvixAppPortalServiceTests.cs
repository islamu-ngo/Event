using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class SvixAppPortalServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid ConsumerId = Guid.Parse("018f0000-0000-7000-8000-000000000050");

    [Test]
    public async Task CreateAccessAsync_WhenConsumerPortalRequested_CreatesApplicationAndPortalAccess()
    {
        var fixture = new Fixture();
        SvixAppPortalAccessRequest? portalRequest = null;
        var before = DateTimeOffset.UtcNow;
        var instanceId = Guid.CreateVersion7();
        var capabilityProfile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            "1.96.1",
            WebhookProviderCapability.AppPortal,
            "svix-1.96.1-v1",
            before);
        var binding = WebhookConsumerProviderBinding.CreatePending(
            TenantId,
            ConsumerId,
            instanceId,
            "production",
            capabilityProfile,
            WebhookProviderCapability.AppPortal);
        binding.VerifyOwnership(TenantId, ConsumerId, "app_custom", before);

        var consumer = new WebhookConsumer
        {
            Id = ConsumerId,
            TenantId = TenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "Community site",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Svix,
            CreatedAt = DateTime.UtcNow
        };
        fixture.ConsumerRepository.GetByIdForOwnerOperationAsync(
                ConsumerId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(consumer);
        fixture.BindingRepository.GetVerifiedByConsumerAsync(
                TenantId,
                ConsumerId,
                WebhookProviderKind.Svix,
                "production",
                Arg.Any<CancellationToken>())
            .Returns(binding);
        fixture.SvixClient.GetApplicationAsync("app_custom", Arg.Any<CancellationToken>())
            .Returns(new SvixApplicationBindingResult(
                "app_custom",
                binding.ApplicationUid,
                new Dictionary<string, string>
                {
                    ["islamu.tenant_id"] = TenantId.ToString("D"),
                    ["islamu.consumer_id"] = ConsumerId.ToString("D"),
                    ["islamu.owner_id"] = TenantId.ToString("D"),
                    ["islamu.owner_kind_id"] = ((int)WebhookConsumerKind.Tenant).ToString()
                }));
        fixture.SvixClient.CreateAppPortalAccessAsync(
                Arg.Do<SvixAppPortalAccessRequest>(request => portalRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(new SvixAppPortalAccessResult("https://svix.example/app-portal", "portal_token"));

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(
                ConsumerId,
                "session-1",
                TimeSpan.FromHours(2)),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Url).IsEqualTo("https://svix.example/app-portal");
        await Assert.That(result.Token).IsEqualTo("portal_token");
        await Assert.That(result.ExpiresAt).IsNotNull();
        await Assert.That(result.ExpiresAt!.Value).IsGreaterThan(before);
        await Assert.That(result.ExpiresAt.Value).IsLessThan(DateTimeOffset.UtcNow.AddMinutes(61));
        await Assert.That(portalRequest).IsNotNull();
        await Assert.That(portalRequest!.AppId).IsEqualTo("app_custom");
        await Assert.That(portalRequest.SessionId).IsEqualTo("session-1");
        await Assert.That(portalRequest.ReadOnly).IsTrue();
        await Assert.That(portalRequest.ExpiresIn).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(portalRequest.FeatureFlags).IsEquivalentTo(["ViewBase"]);
        await Assert.That(portalRequest.IdempotencyKey).IsEqualTo("svix-portal:app_custom:session-1");
    }

    [Test]
    public async Task CreateAccessAsync_WhenPortalDisabled_ReturnsFailureWithoutCallingSvix()
    {
        var fixture = new Fixture(new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new WebhookSvixOptions { AppPortalEnabled = false }
        });

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(ConsumerId, "session-1", null),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("svix_app_portal_disabled");
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .GetOrCreateApplicationAsync(default!, default);
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .CreateAppPortalAccessAsync(default!, default);
    }

    [Test]
    public async Task CreateAccessAsync_WhenConsumerMissing_ReturnsNotFoundWithoutCreatingPortalAccess()
    {
        var fixture = new Fixture();
        fixture.ConsumerRepository.GetByIdForOwnerOperationAsync(
                ConsumerId,
                false,
                Arg.Any<CancellationToken>())
            .Returns((WebhookConsumer?)null);

        var result = await fixture.Service.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(ConsumerId, "session-1", null),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("webhook_consumer_not_found");
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .CreateAppPortalAccessAsync(default!, default);
    }

    private sealed class Fixture
    {
        public Fixture(WebhookOptions? options = null)
        {
            SvixClient = Substitute.For<ISvixWebhookClient>();
            ConsumerRepository = Substitute.For<IWebhookConsumerRepository>();
            BindingRepository = Substitute.For<IWebhookConsumerProviderBindingRepository>();
            Service = new SvixAppPortalService(
                SvixClient,
                ConsumerRepository,
                BindingRepository,
                new StaticOptionsMonitor<WebhookOptions>(options ?? new WebhookOptions
                {
                    Provider = WebhookOptions.ProviderSvix,
                    Svix = new WebhookSvixOptions
                    {
                        Environment = "production",
                        ProviderVersion = "1.96.1",
                        CapabilityPolicyVersion = "svix-1.96.1-v1"
                    }
                }));
        }

        public ISvixWebhookClient SvixClient { get; }

        public IWebhookConsumerRepository ConsumerRepository { get; }

        public IWebhookConsumerProviderBindingRepository BindingRepository { get; }

        public SvixAppPortalService Service { get; }
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
