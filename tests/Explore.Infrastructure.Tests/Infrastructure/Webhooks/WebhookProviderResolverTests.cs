using System.Diagnostics.Metrics;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Telemetry;
using Explore.Application.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookProviderResolverTests
{
    [Test]
    public async Task ConfigureInfrastructureServices_ReplacesFailClosedPlanResolverWithGovernedScopedResolver()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebhookDeliveryPlanResolver, FailClosedWebhookDeliveryPlanResolver>();
        var configuration = new ConfigurationBuilder().Build();

        services.ConfigureInfrastructureServices(configuration);

        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IWebhookDeliveryPlanResolver))
            .ToList();
        await Assert.That(descriptors.Count).IsEqualTo(1);
        await Assert.That(descriptors.Single().ImplementationType)
            .IsEqualTo(typeof(GovernedWebhookDeliveryPlanResolver));
        await Assert.That(descriptors.Single().Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task DeliveryPlanResolver_WhenLocalAuthorityIsComplete_ReturnsVersionedEndpointSnapshotFacts()
    {
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var consumerRepository = Substitute.For<IWebhookConsumerRepository>();
        var endpointRepository = Substitute.For<IWebhookEndpointRepository>();
        var eventTypeRepository = Substitute.For<IWebhookEventTypeRepository>();
        var secretBindingRepository = Substitute.For<ISecretBindingRepository>();
        var consumer = new WebhookConsumer
        {
            Id = consumerId,
            TenantId = tenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "Local integration",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 4,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ConsumerId = consumerId,
            Url = "https://consumer.example.test/webhooks",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "webhook:endpoint:primary",
            SecretVersion = 7,
            SecretActivatedAt = DateTime.UtcNow.AddHours(-2),
            ConfigurationVersion = 9,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        var eventType = new WebhookEventType
        {
            Id = Guid.CreateVersion7(),
            Name = WebhookEventNames.EventPublished,
            GroupName = "events",
            Description = "Event published",
            SchemaJson = "{}",
            SchemaVersion = 3,
            IsEnabled = true,
            PayloadRetentionDays = 21,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        consumerRepository.GetByIdForOwnerOperationAsync(consumerId, false, Arg.Any<CancellationToken>())
            .Returns(consumer);
        eventTypeRepository.GetByNameAsync(eventType.Name, Arg.Any<CancellationToken>())
            .Returns(eventType);
        endpointRepository.GetActiveSubscribedEndpointsByConsumerAsync(
                tenantId,
                consumerId,
                eventType.Name,
                Arg.Any<CancellationToken>())
            .Returns([endpoint]);
        var capabilityResolver = new WebhookProviderCapabilityResolver(
            new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions
            {
                Provider = WebhookOptions.ProviderLocal
            }));
        var resolver = new GovernedWebhookDeliveryPlanResolver(
            consumerRepository,
            endpointRepository,
            eventTypeRepository,
            secretBindingRepository,
            capabilityResolver,
            CreateRetentionPolicyResolver(),
            new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions
            {
                Provider = WebhookOptions.ProviderLocal
            }),
            TimeProvider.System);
        var context = CreateContext() with
        {
            TenantId = tenantId,
            ConsumerId = consumerId,
            EventType = eventType.Name
        };

        var resolution = await resolver.ResolveAsync(context, CancellationToken.None);

        await Assert.That(resolution.Succeeded).IsTrue();
        await Assert.That(resolution.ConfigurationVersion).IsEqualTo("consumer-v4:event-local-v1");
        await Assert.That(resolution.EventContractVersion).IsEqualTo(3);
        await Assert.That(resolution.RetentionPolicyVersion)
            .IsEqualTo("webhook-retention-v1:i14:o21:a30:d90:p90:l30:u365:r14");
        await Assert.That(resolution.PayloadRetentionUntil).IsEqualTo(context.OccurredAt.AddDays(21));
        await Assert.That(resolution.LocalTargets.Count).IsEqualTo(1);
        await Assert.That(resolution.LocalTargets.Single().EndpointConfigurationVersion).IsEqualTo(9);
        await Assert.That(resolution.LocalTargets.Single().CredentialValidFromUtc)
            .IsEqualTo(new DateTimeOffset(endpoint.SecretActivatedAt));
        await Assert.That(resolution.ProviderTargets).IsEmpty();
    }

    [Test]
    public async Task DeliveryPlanResolver_WhenInstanceConsumerIsTargeted_PreservesSourceTenantAndUsesInstanceConfiguration()
    {
        var sourceTenantId = Guid.CreateVersion7();
        var instanceId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var consumerRepository = Substitute.For<IWebhookConsumerRepository>();
        var endpointRepository = Substitute.For<IWebhookEndpointRepository>();
        var eventTypeRepository = Substitute.For<IWebhookEventTypeRepository>();
        var consumer = new WebhookConsumer
        {
            Id = consumerId,
            InstanceId = instanceId,
            ConsumerKind = WebhookConsumerKind.Instance,
            Name = "Instance integration",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 3,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            InstanceId = instanceId,
            ConsumerId = consumerId,
            Url = "https://instance.example.test/webhooks",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "webhook:instance:primary",
            SecretVersion = 2,
            SecretActivatedAt = DateTime.UtcNow.AddHours(-1),
            ConfigurationVersion = 4,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        var eventType = new WebhookEventType
        {
            Id = Guid.CreateVersion7(),
            Name = WebhookEventNames.EventPublished,
            GroupName = "events",
            Description = "Event published",
            SchemaJson = "{}",
            SchemaVersion = 1,
            IsEnabled = true,
            PayloadRetentionDays = 14,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        consumerRepository.GetByIdForOwnerOperationAsync(consumerId, false, Arg.Any<CancellationToken>())
            .Returns(consumer);
        eventTypeRepository.GetByNameAsync(eventType.Name, Arg.Any<CancellationToken>())
            .Returns(eventType);
        endpointRepository.GetActiveSubscribedEndpointsByConsumerAsync(
                null,
                consumerId,
                eventType.Name,
                Arg.Any<CancellationToken>())
            .Returns([endpoint]);
        var capabilityResolver = new WebhookProviderCapabilityResolver(
            new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions
            {
                Provider = WebhookOptions.ProviderLocal
            }));
        var resolver = new GovernedWebhookDeliveryPlanResolver(
            consumerRepository,
            endpointRepository,
            eventTypeRepository,
            Substitute.For<ISecretBindingRepository>(),
            capabilityResolver,
            CreateRetentionPolicyResolver(),
            new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions
            {
                Provider = WebhookOptions.ProviderLocal
            }),
            TimeProvider.System);
        var context = CreateContext() with
        {
            TenantId = sourceTenantId,
            ConsumerId = consumerId,
            EventType = eventType.Name
        };

        var resolution = await resolver.ResolveAsync(context, CancellationToken.None);

        await Assert.That(resolution.Succeeded).IsTrue();
        await Assert.That(resolution.WebhookConsumerId).IsEqualTo(consumerId);
        await Assert.That(resolution.LocalTargets.Single().Endpoint.InstanceId).IsEqualTo(instanceId);
        await Assert.That(resolution.LocalTargets.Single().Endpoint.TenantId).IsNull();
        await endpointRepository.Received(1).GetActiveSubscribedEndpointsByConsumerAsync(
            null,
            consumerId,
            eventType.Name,
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(WebhookConsumerKind.Tenant)]
    [Arguments(WebhookConsumerKind.Organization)]
    [Arguments(WebhookConsumerKind.Group)]
    [Arguments(WebhookConsumerKind.User)]
    public async Task DeliveryPlanResolver_WhenNonInstanceConsumerBelongsToAnotherTenant_FailsBeforeTargetResolution(
        WebhookConsumerKind ownerKind)
    {
        var sourceTenantId = Guid.CreateVersion7();
        var ownerTenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var consumerRepository = Substitute.For<IWebhookConsumerRepository>();
        var endpointRepository = Substitute.For<IWebhookEndpointRepository>();
        var eventTypeRepository = Substitute.For<IWebhookEventTypeRepository>();
        var consumer = new WebhookConsumer
        {
            Id = consumerId,
            TenantId = ownerTenantId,
            OrganizationId = ownerKind == WebhookConsumerKind.Organization ? ownerId : null,
            GroupId = ownerKind == WebhookConsumerKind.Group ? ownerId : null,
            OwnerUserId = ownerKind == WebhookConsumerKind.User ? ownerId : null,
            ConsumerKind = ownerKind,
            Name = "Foreign tenant integration",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        consumerRepository.GetByIdForOwnerOperationAsync(consumerId, false, Arg.Any<CancellationToken>())
            .Returns(consumer);
        var options = new WebhookOptions { Provider = WebhookOptions.ProviderLocal };
        var resolver = new GovernedWebhookDeliveryPlanResolver(
            consumerRepository,
            endpointRepository,
            eventTypeRepository,
            Substitute.For<ISecretBindingRepository>(),
            new WebhookProviderCapabilityResolver(new StaticOptionsMonitor<WebhookOptions>(options)),
            CreateRetentionPolicyResolver(),
            new StaticOptionsMonitor<WebhookOptions>(options),
            TimeProvider.System);

        var resolution = await resolver.ResolveAsync(
            CreateContext() with
            {
                TenantId = sourceTenantId,
                ConsumerId = consumerId
            },
            CancellationToken.None);

        await Assert.That(resolution.Succeeded).IsFalse();
        await Assert.That(resolution.FailureCategory).IsEqualTo("webhook_consumer_source_tenant_mismatch");
        await eventTypeRepository.DidNotReceiveWithAnyArgs().GetByNameAsync(default!, default);
        await endpointRepository.DidNotReceiveWithAnyArgs()
            .GetActiveSubscribedEndpointsByConsumerAsync(default, default, default!, default);
    }

    [Test]
    public async Task DeliveryPlanResolver_WhenSvixAuthorityIsComplete_UsesOnlyInstanceScopedTokenBinding()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var consumerRepository = Substitute.For<IWebhookConsumerRepository>();
        var endpointRepository = Substitute.For<IWebhookEndpointRepository>();
        var eventTypeRepository = Substitute.For<IWebhookEventTypeRepository>();
        var secretBindingRepository = Substitute.For<ISecretBindingRepository>();
        var options = SupportedSelfHostedOptions();
        var capabilityResolver = new WebhookProviderCapabilityResolver(
            new StaticOptionsMonitor<WebhookOptions>(options));
        var capability = capabilityResolver.Resolve(WebhookProviderMode.Svix);
        var profile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            capability.ProviderVersion!,
            capability.ProviderCapabilities,
            capability.ResolutionVersion,
            now.AddMinutes(-1));
        var binding = WebhookConsumerProviderBinding.CreatePending(
            tenantId,
            consumerId,
            Guid.CreateVersion7(),
            capability.ProviderEnvironment!,
            profile,
            capability.ProviderCapabilities);
        binding.VerifyOwnership(tenantId, consumerId, "app_self_hosted_1", now);
        var consumer = new WebhookConsumer
        {
            Id = consumerId,
            TenantId = tenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "Self-hosted Svix integration",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Svix,
            ConfigurationVersion = 2,
            CreatedAt = now.UtcDateTime.AddDays(-1)
        };
        consumer.ProviderBindings.Add(binding);
        var eventType = new WebhookEventType
        {
            Id = Guid.CreateVersion7(),
            Name = WebhookEventNames.EventPublished,
            GroupName = "events",
            Description = "Event published",
            SchemaJson = "{}",
            SchemaVersion = 1,
            IsEnabled = true,
            PayloadRetentionDays = 14,
            CreatedAt = now.UtcDateTime.AddDays(-1)
        };
        var tokenBinding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
            SecretScope.Instance,
            null,
            "WEBHOOKS_SVIX_AUTH_TOKEN");
        tokenBinding.Id = Guid.CreateVersion7();
        tokenBinding.CreatedAt = now.UtcDateTime.AddHours(-1);
        consumerRepository.GetByIdForOwnerOperationAsync(consumerId, false, Arg.Any<CancellationToken>())
            .Returns(consumer);
        eventTypeRepository.GetByNameAsync(eventType.Name, Arg.Any<CancellationToken>())
            .Returns(eventType);
        secretBindingRepository.GetByKeyAndScopeAsync(
                SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
                SecretScope.Instance,
                null,
                Arg.Any<CancellationToken>())
            .Returns(tokenBinding);
        var resolver = new GovernedWebhookDeliveryPlanResolver(
            consumerRepository,
            endpointRepository,
            eventTypeRepository,
            secretBindingRepository,
            capabilityResolver,
            CreateRetentionPolicyResolver(),
            new StaticOptionsMonitor<WebhookOptions>(options),
            new FixedTimeProvider(now));
        var context = CreateContext() with
        {
            TenantId = tenantId,
            ConsumerId = consumerId,
            EventType = eventType.Name,
            OccurredAt = now.AddMinutes(-2)
        };

        var resolution = await resolver.ResolveAsync(context, CancellationToken.None);

        await Assert.That(resolution.Succeeded).IsTrue();
        await Assert.That(resolution.ProviderTargets.Count).IsEqualTo(1);
        await secretBindingRepository.Received(1).GetByKeyAndScopeAsync(
            SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
            SecretScope.Instance,
            null,
            Arg.Any<CancellationToken>());
        await secretBindingRepository.DidNotReceive().GetByKeyAndScopeAsync(
            Arg.Any<string>(),
            SecretScope.Tenant,
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeliveryPlanResolver_AfterLocalToSvixModeChange_KeepsPriorResolutionFrozen()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var consumerRepository = Substitute.For<IWebhookConsumerRepository>();
        var endpointRepository = Substitute.For<IWebhookEndpointRepository>();
        var eventTypeRepository = Substitute.For<IWebhookEventTypeRepository>();
        var secretBindingRepository = Substitute.For<ISecretBindingRepository>();
        var options = SupportedSelfHostedOptions();
        options.AllowTenantOverride = true;
        var capabilityResolver = new WebhookProviderCapabilityResolver(
            new StaticOptionsMonitor<WebhookOptions>(options));
        var svixCapability = capabilityResolver.Resolve(WebhookProviderMode.Svix);
        var profile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            svixCapability.ProviderVersion!,
            svixCapability.ProviderCapabilities,
            svixCapability.ResolutionVersion,
            now.AddMinutes(-5));
        var binding = WebhookConsumerProviderBinding.CreatePending(
            tenantId,
            consumerId,
            Guid.CreateVersion7(),
            svixCapability.ProviderEnvironment!,
            profile,
            svixCapability.ProviderCapabilities);
        binding.VerifyOwnership(tenantId, consumerId, "app_mode_snapshot", now.AddMinutes(-4));
        var consumer = new WebhookConsumer
        {
            Id = consumerId,
            TenantId = tenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "Mode snapshot integration",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 1,
            CreatedAt = now.UtcDateTime.AddDays(-1)
        };
        consumer.ProviderBindings.Add(binding);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ConsumerId = consumerId,
            Url = "https://consumer.example.test/webhooks",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "webhook:endpoint:primary",
            SecretVersion = 1,
            SecretActivatedAt = now.UtcDateTime.AddHours(-1),
            ConfigurationVersion = 1,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = now.UtcDateTime.AddDays(-1)
        };
        var eventType = new WebhookEventType
        {
            Id = Guid.CreateVersion7(),
            Name = WebhookEventNames.EventPublished,
            GroupName = "events",
            Description = "Event published",
            SchemaJson = "{}",
            SchemaVersion = 1,
            IsEnabled = true,
            PayloadRetentionDays = 14,
            CreatedAt = now.UtcDateTime.AddDays(-1)
        };
        var tokenBinding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
            SecretScope.Instance,
            null,
            "WEBHOOKS_SVIX_AUTH_TOKEN");
        tokenBinding.Id = Guid.CreateVersion7();
        tokenBinding.CreatedAt = now.UtcDateTime.AddHours(-1);
        consumerRepository.GetByIdForOwnerOperationAsync(consumerId, false, Arg.Any<CancellationToken>())
            .Returns(consumer);
        eventTypeRepository.GetByNameAsync(eventType.Name, Arg.Any<CancellationToken>())
            .Returns(eventType);
        endpointRepository.GetActiveSubscribedEndpointsByConsumerAsync(
                tenantId,
                consumerId,
                eventType.Name,
                Arg.Any<CancellationToken>())
            .Returns([endpoint]);
        secretBindingRepository.GetByKeyAndScopeAsync(
                SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
                SecretScope.Instance,
                null,
                Arg.Any<CancellationToken>())
            .Returns(tokenBinding);
        var resolver = new GovernedWebhookDeliveryPlanResolver(
            consumerRepository,
            endpointRepository,
            eventTypeRepository,
            secretBindingRepository,
            capabilityResolver,
            CreateRetentionPolicyResolver(),
            new StaticOptionsMonitor<WebhookOptions>(options),
            new FixedTimeProvider(now));
        var context = CreateContext() with
        {
            TenantId = tenantId,
            ConsumerId = consumerId,
            EventType = eventType.Name,
            OccurredAt = now.AddMinutes(-2)
        };

        var localResolution = await resolver.ResolveAsync(context, CancellationToken.None);
        consumer.ChangeProviderMode(WebhookProviderMode.Svix, now.UtcDateTime);
        var svixResolution = await resolver.ResolveAsync(context, CancellationToken.None);

        await Assert.That(localResolution.Succeeded).IsTrue();
        await Assert.That(localResolution.ProviderMode).IsEqualTo(WebhookProviderMode.Local);
        await Assert.That(localResolution.ConfigurationVersion).IsEqualTo("consumer-v1:event-local-v1");
        await Assert.That(localResolution.LocalTargets.Count).IsEqualTo(1);
        await Assert.That(localResolution.ProviderTargets).IsEmpty();
        await Assert.That(svixResolution.Succeeded).IsTrue();
        await Assert.That(svixResolution.ProviderMode).IsEqualTo(WebhookProviderMode.Svix);
        await Assert.That(svixResolution.ConfigurationVersion)
            .IsEqualTo($"consumer-v2:{svixCapability.ResolutionVersion}");
        await Assert.That(svixResolution.LocalTargets).IsEmpty();
        await Assert.That(svixResolution.ProviderTargets.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CapabilityResolver_WhenSelfHostedSvixProfileIsPinned_ReturnsOnlyProvenCapabilities()
    {
        var resolver = new WebhookProviderCapabilityResolver(
            new StaticOptionsMonitor<WebhookOptions>(SupportedSelfHostedOptions()));

        var resolution = resolver.Resolve(WebhookProviderMode.Svix);

        await Assert.That(resolution.IsProviderModeAvailable).IsTrue();
        await Assert.That(resolution.LocalCapabilities).IsEqualTo(WebhookProviderCapability.None);
        await Assert.That(resolution.ProviderCapabilities).IsEqualTo(
            WebhookProviderCapability.EndpointManagement |
            WebhookProviderCapability.PayloadInspection |
            WebhookProviderCapability.AppPortal |
            WebhookProviderCapability.EventCatalog);
        await Assert.That(resolution.ProviderCapabilities.HasFlag(WebhookProviderCapability.Replay)).IsFalse();
        await Assert.That(resolution.ProviderEnvironment)
            .IsEqualTo(SvixConformanceProfileRegistry.SelfHostedEnvironment);
        await Assert.That(resolution.ProviderVersion)
            .IsEqualTo(SvixConformanceProfileRegistry.SelfHostedProviderVersion);
    }

    [Test]
    public async Task CapabilityResolver_WhenLocalModeSelected_DoesNotClaimProviderNativeParity()
    {
        var resolver = new WebhookProviderCapabilityResolver(
            new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions()));

        var resolution = resolver.Resolve(WebhookProviderMode.Local);

        await Assert.That(resolution.IsProviderModeAvailable).IsTrue();
        await Assert.That(resolution.LocalCapabilities).IsEqualTo(
            WebhookProviderCapability.EndpointManagement |
            WebhookProviderCapability.EventCatalog);
        await Assert.That(resolution.ProviderCapabilities).IsEqualTo(WebhookProviderCapability.None);
        await Assert.That(resolution.SupportsLocalConfiguration(WebhookProviderCapability.EndpointManagement)).IsTrue();
        await Assert.That(resolution.SupportsLocalConfiguration(WebhookProviderCapability.ProviderAttemptVisibility)).IsFalse();
    }

    [Test]
    public async Task CapabilityResolver_WhenTenantOverrideIsLocked_RejectsDifferentMode()
    {
        var options = new WebhookOptions
        {
            Provider = WebhookOptions.ProviderLocal,
            AllowTenantOverride = false
        };
        var resolver = new WebhookProviderCapabilityResolver(new StaticOptionsMonitor<WebhookOptions>(options));

        var resolution = resolver.Resolve(WebhookProviderMode.Svix);

        await Assert.That(resolution.IsProviderModeAvailable).IsFalse();
        await Assert.That(resolution.UnavailableReasonCode)
            .IsEqualTo("webhook_provider_tenant_override_disabled");
    }

    [Test]
    public async Task Publisher_WhenDryRunPlanIsResolved_MaterializesCanonicalMessageWithoutProviderIdentifier()
    {
        var planResolver = Substitute.For<IWebhookDeliveryPlanResolver>();
        var materializer = Substitute.For<IWebhookDeliveryPlanMaterializer>();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        var context = CreateContext();
        var materializedAt = context.OccurredAt.AddMinutes(1);
        WebhookDeliveryMaterialization? captured = null;
        planResolver.ResolveAsync(context, Arg.Any<CancellationToken>())
            .Returns(WebhookDeliveryPlanResolution.Success(
                context.ConsumerId!.Value,
                WebhookProviderMode.DryRun,
                "dry-run-v1",
                1,
                "standard",
                "retention-v1",
                context.OccurredAt.AddDays(14),
                context.OccurredAt.AddDays(30),
                context.OccurredAt.AddDays(90),
                context.OccurredAt.AddDays(90).UtcDateTime,
                context.OccurredAt.AddDays(30)));
        materializer.MaterializeAsync(
                Arg.Do<WebhookDeliveryMaterialization>(value => captured = value),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var value = call.Arg<WebhookDeliveryMaterialization>();
                return new WebhookDeliveryMaterializationResult(
                    value.Message,
                    value.DeliveryPlan,
                    Created: true);
            });
        var publisher = new DefaultWebhookEventPublisher(
            new DefaultWebhookPayloadBuilder(new WebhookEventTypeRegistry()),
            planResolver,
            materializer,
            new BusinessMetrics(meterFactory),
            new FixedTimeProvider(materializedAt));

        var result = await publisher.PublishAsync(context, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.MessageId).IsEqualTo(captured!.Message.Id);
        await Assert.That(result.MessageId).IsNotEqualTo(context.MessageId);
        await Assert.That(result.ProviderMessageId).IsNull();
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.DeliveryPlan.ProviderMode).IsEqualTo(WebhookProviderMode.DryRun);
        await Assert.That(captured.ProviderPublications).IsEmpty();
        await Assert.That(captured.LocalTargets).IsEmpty();
        await Assert.That(Encoding.UTF8.GetString(captured.Message.GetPayloadBytes()!))
            .Contains("\"event.published\"");
    }

    private static WebhookEventBuildContext CreateContext()
    {
        var messageId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var aggregateId = Guid.CreateVersion7();

        return new WebhookEventBuildContext(
            messageId,
            tenantId,
            WebhookEventNames.EventPublished,
            "domain-event-1",
            "Event",
            aggregateId,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?>
            {
                ["eventId"] = aggregateId.ToString("D"),
                ["status"] = "Published",
                ["publicUrl"] = "https://example.org/events/community-iftar"
            },
            Guid.CreateVersion7());
    }

    private static WebhookOptions SupportedSelfHostedOptions() =>
        new()
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

    private static WebhookRetentionPolicyResolver CreateRetentionPolicyResolver() =>
        new(new StaticOptionsMonitor<WebhookRetentionSettings>(new WebhookRetentionSettings()));

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
