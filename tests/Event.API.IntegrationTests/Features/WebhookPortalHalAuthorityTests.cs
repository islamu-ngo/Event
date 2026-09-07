using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Hateoas;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.API.IntegrationTests.Features;

public sealed class WebhookPortalHalAuthorityTests
{
    private static readonly Guid TenantId = Guid.Parse("0190f8c6-5031-7000-8000-000000000001");
    private static readonly Guid ConsumerId = Guid.Parse("0190f8c6-5031-7000-8000-000000000002");

    [Test]
    public async Task VerifiedPersistedBindingAndAuthorization_EmitPortalLink()
    {
        var fixture = new Fixture([CreateBinding()]);

        var resource = await fixture.Assembler.ToResource(CreateConsumerDto(), fixture.HttpContext);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsTrue();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.RepairProviderBinding)).IsTrue();
        await Assert.That(resource.Links[LinkRelations.OpenProviderPortal].Method).IsEqualTo("POST");
        await fixture.AuthorizationProvider.Received(1).AuthorizeBatchAsync(
            Arg.Is<IReadOnlyList<AuthorizationRequest>>(checks => checks != null && checks.Any(check =>
                check.Action == AuthorizationActions.Webhooks.OpenProviderPortal &&
                check.ResourceId == ConsumerId.ToString("D"))),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MissingPersistedBinding_OmitsPortalLink()
    {
        var fixture = new Fixture([]);

        var resource = await fixture.Assembler.ToResource(
            CreateConsumerDto(capabilityAuthorityAvailable: false),
            fixture.HttpContext);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsFalse();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.RepairProviderBinding)).IsTrue();
    }

    [Test]
    public async Task MissingAuthoritativeAppPortalCapability_OmitsPortalLinkWithoutEligibilityLookup()
    {
        var fixture = new Fixture([CreateBinding()]);

        var resource = await fixture.Assembler.ToResource(
            CreateConsumerDto(appPortalAvailable: false),
            fixture.HttpContext);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsFalse();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.RepairProviderBinding)).IsTrue();
        await fixture.BindingRepository.DidNotReceive().GetVerifiedByConsumersAsync(
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<WebhookProviderKind>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnsupportedProviderVersion_OmitsPortalLink()
    {
        var fixture = new Fixture([CreateBinding(providerVersion: "unknown")]);

        var resource = await fixture.Assembler.ToResource(CreateConsumerDto(), fixture.HttpContext);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsFalse();
    }

    [Test]
    public async Task ProviderOrGovernanceWithoutAppPortal_OmitsPortalLink()
    {
        var providerFixture = new Fixture([CreateBinding(
            providerCapabilities: WebhookProviderCapability.EndpointManagement)]);
        var governanceFixture = new Fixture([CreateBinding(
            governanceCapabilities: WebhookProviderCapability.EndpointManagement)]);

        var providerResource = await providerFixture.Assembler.ToResource(
            CreateConsumerDto(),
            providerFixture.HttpContext);
        var governanceResource = await governanceFixture.Assembler.ToResource(
            CreateConsumerDto(),
            governanceFixture.HttpContext);

        await Assert.That(providerResource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsFalse();
        await Assert.That(governanceResource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsFalse();
    }

    [Test]
    public async Task AuthorizationDenied_OmitsPortalLink()
    {
        var fixture = new Fixture([CreateBinding()], authorizePortal: false);

        var resource = await fixture.Assembler.ToResource(CreateConsumerDto(), fixture.HttpContext);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsFalse();
    }

    [Test]
    public async Task PersistenceFailure_FailsClosedWithoutPortalLink()
    {
        var fixture = new Fixture([CreateBinding()]);
        fixture.BindingRepository.GetVerifiedByConsumersAsync(
                TenantId,
                Arg.Any<IReadOnlyCollection<Guid>>(),
                WebhookProviderKind.Svix,
                SvixConformanceProfileRegistry.SelfHostedEnvironment,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<WebhookConsumerProviderBinding>>(
                new InvalidOperationException("database unavailable")));

        var resource = await fixture.Assembler.ToResource(CreateConsumerDto(), fixture.HttpContext);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsFalse();
    }

    [Test]
    public async Task CollectionEligibility_UsesOneTenantScopedBatch()
    {
        var secondConsumerId = Guid.Parse("0190f8c6-5031-7000-8000-000000000003");
        var fixture = new Fixture([CreateBinding()]);
        var first = CreateConsumerDto();
        var second = CreateConsumerDto(secondConsumerId);

        var collection = await fixture.Assembler.ToCollectionResource(
            new[] { first, second },
            RouteNames.GetWebhookConsumers,
            fixture.HttpContext);

        var firstResource = collection.Embedded.Items.Single(item => item.Data.Id == ConsumerId);
        var secondResource = collection.Embedded.Items.Single(item => item.Data.Id == secondConsumerId);
        await Assert.That(firstResource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsTrue();
        await Assert.That(secondResource.Links.ContainsKey(LinkRelations.OpenProviderPortal)).IsFalse();
        await fixture.BindingRepository.Received(1).GetVerifiedByConsumersAsync(
            TenantId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids != null && ids.Count == 2 && ids.Contains(ConsumerId) && ids.Contains(secondConsumerId)),
            WebhookProviderKind.Svix,
            SvixConformanceProfileRegistry.SelfHostedEnvironment,
            Arg.Any<CancellationToken>());
    }

    private static WebhookConsumerProviderBinding CreateBinding(
        WebhookProviderCapability providerCapabilities = WebhookProviderCapability.AppPortal,
        WebhookProviderCapability governanceCapabilities = WebhookProviderCapability.AppPortal,
        string providerVersion = SvixConformanceProfileRegistry.SelfHostedProviderVersion,
        string capabilityPolicyVersion = SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion)
    {
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
            SvixConformanceProfileRegistry.SelfHostedEnvironment,
            profile,
            governanceCapabilities);
        binding.VerifyOwnership(TenantId, ConsumerId, "app_hal_authority", DateTimeOffset.UtcNow);
        return binding;
    }

    private static WebhookConsumerDto CreateConsumerDto(
        Guid? consumerId = null,
        bool capabilityAuthorityAvailable = true,
        bool appPortalAvailable = true) => new()
        {
            Id = consumerId ?? ConsumerId,
            TenantId = TenantId,
            ConsumerKindId = (int)WebhookConsumerKind.Tenant,
            ConsumerKindCode = "TENANT",
            ConsumerKindName = nameof(WebhookConsumerKind.Tenant),
            StatusId = (int)WebhookConsumerStatus.Active,
            StatusCode = "ACTIVE",
            StatusName = nameof(WebhookConsumerStatus.Active),
            ProviderModeId = (int)WebhookProviderMode.Svix,
            ProviderModeCode = "SVIX",
            ProviderModeName = nameof(WebhookProviderMode.Svix),
            ProviderCapabilityAuthorityAvailable = capabilityAuthorityAvailable,
            CapabilityResolutionVersion = SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion,
            CapabilityUnavailableReasonCode = capabilityAuthorityAvailable
            ? null
            : "webhook_provider_binding_unverified",
            ProviderCapabilities =
        [
            new WebhookProviderCapabilityDto
            {
                CapabilityId = (int)WebhookProviderCapability.AppPortal,
                CapabilityCode = "APP_PORTAL",
                CapabilityName = "App portal",
                IsAvailable = appPortalAvailable,
                AvailableFromProviderCodes = appPortalAvailable ? ["SVIX"] : [],
                UnavailableReasonCode = appPortalAvailable
                    ? null
                    : "webhook_provider_capability_unproven"
            }
        ],
            Name = "HAL authority consumer",
            CreatedAt = DateTime.UtcNow
        };

    private sealed class Fixture
    {
        public Fixture(
            IReadOnlyList<WebhookConsumerProviderBinding> bindings,
            bool authorizePortal = true)
        {
            BindingRepository = Substitute.For<IWebhookConsumerProviderBindingRepository>();
            BindingRepository.GetVerifiedByConsumersAsync(
                    TenantId,
                    Arg.Any<IReadOnlyCollection<Guid>>(),
                    WebhookProviderKind.Svix,
                    SvixConformanceProfileRegistry.SelfHostedEnvironment,
                    Arg.Any<CancellationToken>())
                .Returns(bindings);

            AuthorizationProvider = Substitute.For<IAuthorizationProvider>();
            AuthorizationProvider.AuthorizeBatchAsync(
                    Arg.Any<IReadOnlyList<AuthorizationRequest>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<IReadOnlyList<AuthorizationDecision>>(
                    call.ArgAt<IReadOnlyList<AuthorizationRequest>>(0)
                        .Select(check => check.Action != AuthorizationActions.Webhooks.OpenProviderPortal || authorizePortal
                            ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                            : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local))
                        .ToArray()));

            var evaluator = new HateoasAuthorizationEvaluator(
                AuthorizationProvider,
                NSubstitute.Substitute.For<Explore.Application.Contracts.Persistence.IEventRepository>(),
                NSubstitute.Substitute.For<ITenantContext>(),
                NullLogger<HateoasAuthorizationEvaluator>.Instance);
            var services = new ServiceCollection().AddSingleton<IHateoasAuthorizationEvaluator>(evaluator);
            HttpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider(),
                User = new ClaimsPrincipal(new ClaimsIdentity([], "TestAuth"))
            };

            var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
            linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
                .Returns(call =>
                {
                    var definition = call.ArgAt<LinkDefinition>(0);
                    return new HalLink
                    {
                        Href = definition.Rel == LinkRelations.OpenProviderPortal
                            ? "/api/webhooks/svix/app-portal"
                            : $"/api/webhooks/{definition.Rel}",
                        Method = definition.Method,
                        Title = definition.Title
                    };
                });
            linkGenerator.GeneratePath(
                    Arg.Any<string>(),
                    Arg.Any<object?>(),
                    Arg.Any<HttpContext>())
                .Returns("/api/webhooks/consumers");

            var eligibilityService = new SvixPortalEligibilityService(
                BindingRepository,
                new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions
                {
                    Provider = WebhookOptions.ProviderSvix,
                    Svix = new WebhookSvixOptions
                    {
                        BaseUrl = "http://svix.test",
                        Environment = SvixConformanceProfileRegistry.SelfHostedEnvironment,
                        ProviderVersion = SvixConformanceProfileRegistry.SelfHostedProviderVersion,
                        CapabilityPolicyVersion = SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion
                    }
                }),
                NullLogger<SvixPortalEligibilityService>.Instance);
            BindingAuthorityService = Substitute.For<IWebhookProviderBindingAuthorityService>();
            var capabilityProfile = WebhookProviderCapabilityProfile.Create(
                WebhookProviderKind.Svix,
                SvixConformanceProfileRegistry.SelfHostedProviderVersion,
                WebhookProviderCapability.None,
                SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion,
                DateTimeOffset.UtcNow);
            BindingAuthorityService.ResolveCurrentProfile().Returns(
                WebhookProviderBindingProfileResult.Success(new WebhookProviderBindingProfile(
                    WebhookProviderKind.Svix,
                    SvixConformanceProfileRegistry.SelfHostedEnvironment,
                    capabilityProfile,
                    WebhookProviderCapability.None)));
            Assembler = new WebhookConsumerResourceAssembler(
                linkGenerator,
                new WebhookConsumerDetailLinkPolicy(),
                new WebhookConsumerCollectionLinkPolicy(new WebhookConsumerDetailLinkPolicy()),
                eligibilityService,
                BindingAuthorityService);
        }

        public IWebhookConsumerProviderBindingRepository BindingRepository { get; }
        public IWebhookProviderBindingAuthorityService BindingAuthorityService { get; }
        public IAuthorizationProvider AuthorizationProvider { get; }
        public DefaultHttpContext HttpContext { get; }
        public WebhookConsumerResourceAssembler Assembler { get; }
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
