using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class WebhookAggregateSchemaInvariantTests
{
    [Test]
    public async Task AggregateAndLookupIdentifiers_UseCanonicalTypes()
    {
        Type[] aggregateTypes =
        [
            typeof(WebhookMessage),
            typeof(WebhookDeliveryPlanSnapshot),
            typeof(WebhookLocalTargetSnapshot),
            typeof(WebhookProviderPublication),
            typeof(WebhookProviderPublicationAttempt),
            typeof(WebhookConsumerProviderBinding),
            typeof(IncomingWebhookMessage),
            typeof(IncomingWebhookEffectReceipt),
            typeof(IncomingWebhookProcessingAttempt),
            typeof(IncomingWebhookRedriveRecord)
        ];

        foreach (var aggregateType in aggregateTypes)
        {
            await Assert.That(aggregateType.GetProperty("Id")?.PropertyType)
                .IsEqualTo(typeof(Guid));
        }

        var lookupTypes = typeof(WebhookConsumer).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(WebhookConsumer).Namespace)
            .Where(type => type.Name.StartsWith("Webhook", StringComparison.Ordinal)
                || type.Name.StartsWith("IncomingWebhook", StringComparison.Ordinal))
            .Where(type => type.Name.EndsWith("Lookup", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(lookupTypes.Length).IsGreaterThanOrEqualTo(10);
        foreach (var lookupType in lookupTypes)
        {
            await Assert.That(lookupType.GetProperty("Id")?.PropertyType)
                .IsEqualTo(typeof(int));
        }
    }

    [Test]
    public async Task QueryGovernedStates_AreStoredAsRequiredIntegerForeignKeys()
    {
        var expectedStateOwners = new (Type Entity, string IdProperty, string WrapperProperty)[]
        {
            (typeof(WebhookConsumer), "ConsumerKindId", "ConsumerKind"),
            (typeof(WebhookConsumer), "StatusId", "Status"),
            (typeof(WebhookConsumer), "ProviderModeId", "ProviderMode"),
            (typeof(WebhookConsumerProviderBinding), "ProviderKindId", "ProviderKind"),
            (typeof(WebhookEndpoint), "StatusId", "Status"),
            (typeof(WebhookLocalTargetSnapshot), "DeliveryStatusId", "DeliveryStatus"),
            (typeof(WebhookDeliveryAttempt), "OutcomeId", "Outcome"),
            (typeof(IncomingWebhookMessage), "StatusId", "Status"),
            (typeof(WebhookProviderPublication), "StatusId", "Status")
        };

        foreach (var expected in expectedStateOwners)
        {
            await Assert.That(expected.Entity.GetProperty(expected.IdProperty)?.PropertyType)
                .IsEqualTo(typeof(int));
            await Assert.That(expected.Entity.GetProperty(expected.WrapperProperty)?
                    .GetCustomAttributes(typeof(NotMappedAttribute), inherit: true).Length)
                .IsEqualTo(1);
        }
    }

    [Test]
    public async Task Publication_OwnsProviderStateAndReferencesImmutablePlan()
    {
        await Assert.That(typeof(WebhookProviderPublication)
                .GetProperty("WebhookDeliveryPlanSnapshotId")?.PropertyType)
            .IsEqualTo(typeof(Guid));
        await Assert.That(typeof(WebhookProviderPublication)
                .GetProperty("PublicationFence")?.PropertyType)
            .IsEqualTo(typeof(long));
        await Assert.That(typeof(WebhookProviderPublication)
                .GetProperty("ConcurrencyVersion")?.PropertyType)
            .IsEqualTo(typeof(long));

        await Assert.That(typeof(WebhookMessage).GetProperty("Status")).IsNull();
        await Assert.That(typeof(WebhookMessage).GetProperty("ProviderMessageId")).IsNull();
    }

    [Test]
    public async Task Factories_CreateVersionSevenIdentifiers_AndRejectInvalidOwnership()
    {
        var now = DomainTestClock.UtcNowOffset;
        var plan = WebhookDeliveryPlanSnapshot.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            WebhookProviderMode.Local,
            "configuration-v1",
            "contract-v1",
            "default",
            "retention-v1",
            now.AddDays(1),
            now.AddDays(30),
            now.AddDays(90),
            now.AddDays(90),
            now.AddDays(30),
            now);

        await Assert.That(plan.Id.Version).IsEqualTo(7);
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            WebhookDeliveryPlanSnapshot.Create(
                Guid.Empty,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                WebhookProviderMode.Local,
                "configuration-v1",
                "contract-v1",
                "default",
                "retention-v1",
                now.AddDays(1),
                now.AddDays(30),
                now.AddDays(90),
                now.AddDays(90),
                now.AddDays(30),
                now)));
    }

    [Test]
    public async Task PendingLocalTarget_ExplicitMigrationAdvancesOnlyItsFrozenConfigurationFacts()
    {
        var now = DomainTestClock.UtcNowOffset;
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var plan = WebhookDeliveryPlanSnapshot.Create(
            tenantId,
            messageId,
            consumerId,
            WebhookProviderMode.Local,
            "consumer-v1",
            "contract-v1",
            "standard",
            "retention-v1",
            now.AddDays(14),
            now.AddDays(30),
            now.AddDays(90),
            now.AddDays(90),
            now.AddDays(30),
            now);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ConsumerId = consumerId,
            Url = "https://old.example.test/webhooks",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "webhook:endpoint:v1",
            SecretVersion = 1,
            ConfigurationVersion = 1,
            MaxAttempts = 5,
            TimeoutSeconds = 10,
            CreatedAt = now.UtcDateTime.AddDays(-1)
        };
        var target = WebhookLocalTargetSnapshot.Create(
            plan,
            endpoint,
            endpoint.ConfigurationVersion,
            now.AddDays(-1),
            null,
            now);
        endpoint.UpdateConfiguration(
            "https://new.example.test/webhooks",
            "new configuration",
            9,
            20,
            300,
            now.UtcDateTime.AddMinutes(1));

        target.MigratePendingConfiguration(endpoint, now.AddMinutes(1));

        await Assert.That(target.EndpointConfigurationVersion).IsEqualTo(2);
        await Assert.That(target.DestinationUrl).IsEqualTo("https://new.example.test/webhooks");
        await Assert.That(target.CredentialReference).IsEqualTo("webhook:endpoint:v1");
        await Assert.That(target.CredentialVersion).IsEqualTo(1);
        await Assert.That(target.MaxAttempts).IsEqualTo(9);
        await Assert.That(target.TimeoutSeconds).IsEqualTo(20);
        await Assert.That(target.RateLimitPerMinute).IsEqualTo(300);
        await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
    }

    [Test]
    public async Task EndpointCredentialRotation_RecordsDedicatedActivationTime()
    {
        var activatedAt = DomainTestClock.UtcNow;
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            ConsumerId = Guid.CreateVersion7(),
            Url = "https://consumer.example.test/webhook",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "webhook:endpoint:v1",
            SecretVersion = 1,
            SecretActivatedAt = activatedAt.AddDays(-1),
            ConfigurationVersion = 1,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = activatedAt.AddDays(-1)
        };

        endpoint.RotateSigningCredential(
            "webhook:endpoint:v2",
            activatedAt.AddDays(1),
            activatedAt);

        await Assert.That(endpoint.SecretActivatedAt).IsEqualTo(activatedAt);
        await Assert.That(endpoint.SecretVersion).IsEqualTo(2);
        await Assert.That(endpoint.ConfigurationVersion).IsEqualTo(2);
    }
}
