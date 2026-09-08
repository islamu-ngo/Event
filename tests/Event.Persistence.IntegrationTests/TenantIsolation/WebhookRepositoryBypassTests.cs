using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class WebhookRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task TenantOperationBypasses_WithAmbientTenant_ReturnAndUpdateOnlyExplicitWebhookRows()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("webhook-tenant-a");
        var tenantB = CreateTenant("webhook-tenant-b");
        var publishedType = CreateEventType("event.published");
        var updatedType = CreateEventType("event.updated");
        var consumerA = CreateConsumer(tenantA.Id, "Tenant A Consumer", WebhookProviderMode.Local, "shared-app");
        var consumerB = CreateConsumer(tenantB.Id, "Tenant B Consumer", WebhookProviderMode.Local, "tenant-b-app");
        var endpointA = CreateEndpoint(tenantA.Id, consumerA.Id, "tenant-a", WebhookEndpointStatus.Active);
        var disabledEndpointA = CreateEndpoint(tenantA.Id, consumerA.Id, "tenant-a-disabled", WebhookEndpointStatus.Disabled);
        var wrongEventEndpointA = CreateEndpoint(tenantA.Id, consumerA.Id, "tenant-a-wrong-event", WebhookEndpointStatus.Active);
        var endpointB = CreateEndpoint(tenantB.Id, consumerB.Id, "tenant-b", WebhookEndpointStatus.Active);
        var messageA = CreateMessage(tenantA.Id, consumerA.Id, "evt-a");
        var messageB = CreateMessage(tenantB.Id, consumerB.Id, "evt-b");
        var attemptA = CreateDeliveryAttempt(
            tenantA.Id,
            messageA.Id,
            endpointA.Id,
            WebhookDeliveryAttemptOutcome.Failed,
            DateTime.UtcNow.AddMinutes(-10));
        var attemptB = CreateDeliveryAttempt(
            tenantB.Id,
            messageB.Id,
            endpointB.Id,
            WebhookDeliveryAttemptOutcome.Failed,
            DateTime.UtcNow.AddMinutes(-10));
        var incomingA = CreateIncomingMessage(tenantA.Id, "coop", "provider-shared", "idem-a");
        var incomingB = CreateIncomingMessage(tenantB.Id, "coop", "provider-shared", "idem-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        seedContext.WebhookEventTypes.AddRange(publishedType, updatedType);
        seedContext.WebhookConsumers.AddRange(consumerA, consumerB);
        seedContext.WebhookEndpoints.AddRange(endpointA, disabledEndpointA, wrongEventEndpointA, endpointB);
        seedContext.WebhookEndpointSubscriptions.AddRange(
            CreateSubscription(tenantA.Id, endpointA.Id, publishedType.Id),
            CreateSubscription(tenantA.Id, disabledEndpointA.Id, publishedType.Id),
            CreateSubscription(tenantA.Id, wrongEventEndpointA.Id, updatedType.Id),
            CreateSubscription(tenantB.Id, endpointB.Id, publishedType.Id));
        seedContext.WebhookMessages.AddRange(messageA, messageB);
        seedContext.WebhookDeliveryAttempts.AddRange(attemptA, attemptB);
        seedContext.IncomingWebhookMessages.AddRange(incomingA, incomingB);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleConsumers = await tenantBContext.WebhookConsumers.AsNoTracking().Select(row => row.Id).ToListAsync();
        var visibleEndpoints = await tenantBContext.WebhookEndpoints.AsNoTracking().Select(row => row.Id).ToListAsync();
        var visibleMessages = await tenantBContext.WebhookMessages.AsNoTracking().Select(row => row.Id).ToListAsync();
        var visibleAttempts = await tenantBContext.WebhookDeliveryAttempts.AsNoTracking().Select(row => row.Id).ToListAsync();
        var visibleIncoming = await tenantBContext.IncomingWebhookMessages.AsNoTracking().Select(row => row.Id).ToListAsync();
        var consumerRepository = new WebhookConsumerRepository(tenantBContext);
        var endpointRepository = new WebhookEndpointRepository(tenantBContext);
        var messageRepository = new WebhookMessageRepository(tenantBContext);
        var attemptRepository = new WebhookDeliveryAttemptRepository(tenantBContext);
        var incomingRepository = new IncomingWebhookMessageRepository(tenantBContext);

        var tenantAConsumers = await consumerRepository.ListByTenantAsync(tenantA.Id, 10, CancellationToken.None);
        var tenantAConsumerByApp = await consumerRepository.GetByExternalProviderAppIdAsync(
            tenantA.Id,
            "shared-app",
            CancellationToken.None);
        var tenantAActiveEndpoints = await endpointRepository.GetActiveSubscribedEndpointsAsync(
            tenantA.Id,
            "event.published",
            WebhookProviderMode.Local,
            CancellationToken.None);
        var wrongTenantEndpoint = await endpointRepository.GetByTenantAndIdAsync(
            tenantB.Id,
            endpointA.Id,
            CancellationToken.None);
        var tenantAMessages = await messageRepository.ListByTenantAsync(tenantA.Id, 10, CancellationToken.None);
        var wrongTenantMessage = await messageRepository.GetByTenantAndIdAsync(
            tenantB.Id,
            messageA.Id,
            CancellationToken.None);
        var tenantAAttempts = await attemptRepository.ListByTenantAsync(
            tenantA.Id,
            messageId: null,
            endpointId: null,
            limit: 10,
            CancellationToken.None);
        var tenantAAttemptsForForeignEndpoint = await attemptRepository.ListByTenantAsync(
            tenantA.Id,
            messageId: null,
            endpointB.Id,
            limit: 10,
            CancellationToken.None);
        var tenantAIncoming = await incomingRepository.GetByProviderMessageIdForUpdateAsync(
            tenantA.Id,
            "coop",
            "provider-shared",
            CancellationToken.None);
        var changedAt = new DateTime(2026, 1, 7, 12, 0, 0, DateTimeKind.Utc);
        await endpointRepository.ArchiveAsync(tenantA.Id, disabledEndpointA.Id, changedAt, CancellationToken.None);
        var incomingLeaseToken = Guid.CreateVersion7();
        tenantAIncoming!.Claim("bypass-test", incomingLeaseToken, changedAt.AddMinutes(1), changedAt);
        tenantAIncoming.Ignore(
            incomingLeaseToken,
            tenantAIncoming.ProcessingFence,
            tenantAIncoming.ProcessingGeneration,
            "test_settled",
            "Tenant-scoped transition completed.",
            changedAt.AddSeconds(1));
        await incomingRepository.SaveChangesAsync(CancellationToken.None);

        await using var verifyContext = fixture.CreateDbContext();
        var endpoints = await verifyContext.WebhookEndpoints
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => row.Id == disabledEndpointA.Id || row.Id == endpointB.Id)
            .ToDictionaryAsync(row => row.Id);
        var messages = await verifyContext.WebhookMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => row.Id == messageA.Id || row.Id == messageB.Id)
            .ToDictionaryAsync(row => row.Id);
        var incomingMessages = await verifyContext.IncomingWebhookMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => row.Id == incomingA.Id || row.Id == incomingB.Id)
            .ToDictionaryAsync(row => row.Id);

        await Assert.That(visibleConsumers).IsEquivalentTo([consumerB.Id]);
        await Assert.That(visibleEndpoints).IsEquivalentTo([endpointB.Id]);
        await Assert.That(visibleMessages).IsEquivalentTo([messageB.Id]);
        await Assert.That(visibleAttempts).IsEquivalentTo([attemptB.Id]);
        await Assert.That(visibleIncoming).IsEquivalentTo([incomingB.Id]);
        await Assert.That(tenantAConsumers.Select(row => row.Id)).IsEquivalentTo([consumerA.Id]);
        await Assert.That(tenantAConsumerByApp).IsNotNull();
        await Assert.That(tenantAConsumerByApp!.Id).IsEqualTo(consumerA.Id);
        await Assert.That(tenantAActiveEndpoints.Select(row => row.Id)).IsEquivalentTo([endpointA.Id]);
        await Assert.That(wrongTenantEndpoint).IsNull();
        await Assert.That(tenantAMessages.Select(row => row.Id)).IsEquivalentTo([messageA.Id]);
        await Assert.That(wrongTenantMessage).IsNull();
        await Assert.That(tenantAAttempts.Select(row => row.Id)).IsEquivalentTo([attemptA.Id]);
        await Assert.That(tenantAAttemptsForForeignEndpoint).IsEmpty();
        await Assert.That(tenantAIncoming).IsNotNull();
        await Assert.That(tenantAIncoming!.Id).IsEqualTo(incomingA.Id);
        await Assert.That(endpoints[disabledEndpointA.Id].Status).IsEqualTo(WebhookEndpointStatus.Archived);
        await Assert.That(endpoints[endpointB.Id].Status).IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(messages[messageA.Id].PayloadHash).IsEqualTo(messageA.PayloadHash);
        await Assert.That(messages[messageB.Id].PayloadHash).IsEqualTo(messageB.PayloadHash);
        await Assert.That(incomingMessages[incomingA.Id].Status).IsEqualTo(IncomingWebhookMessageStatus.Ignored);
        await Assert.That(incomingMessages[incomingB.Id].Status).IsEqualTo(IncomingWebhookMessageStatus.Verified);
    }

    [Test]
    public async Task WorkerQueueBypasses_WithAmbientTenant_ClaimAndSettleExactLocalTarget()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();
        var tenantA = CreateTenant("webhook-worker-a");
        var tenantB = CreateTenant("webhook-worker-b");
        var consumerA = CreateConsumer(tenantA.Id, "Tenant A Worker", WebhookProviderMode.Local, "worker-a");
        var consumerB = CreateConsumer(tenantB.Id, "Tenant B Worker", WebhookProviderMode.Local, "worker-b");
        var endpointA = CreateEndpoint(tenantA.Id, consumerA.Id, "worker-a", WebhookEndpointStatus.Active);
        var endpointB = CreateEndpoint(tenantB.Id, consumerB.Id, "worker-b", WebhookEndpointStatus.Active);
        var messageA = CreateMessage(tenantA.Id, consumerA.Id, "due-a");
        var messageB = CreateMessage(tenantB.Id, consumerB.Id, "due-b");
        var now = new DateTimeOffset(2026, 1, 8, 12, 0, 0, TimeSpan.Zero);
        var graphA = CreateTargetGraph(messageA, endpointA, consumerA, now.AddMinutes(-2));
        var graphB = CreateTargetGraph(messageB, endpointB, consumerB, now.AddMinutes(-1));
        seedContext.AddRange(
            tenantA,
            tenantB,
            consumerA,
            consumerB,
            endpointA,
            endpointB,
            messageA,
            messageB,
            graphA.Plan,
            graphB.Plan,
            graphA.Target,
            graphB.Target);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleTargets = await tenantBContext.WebhookLocalTargetSnapshots
            .AsNoTracking()
            .Select(target => target.Id)
            .ToListAsync();
        var repository = new WebhookLocalTargetRepository(tenantBContext);
        var dueTenantIds = await repository.GetDueTenantIdsAsync(10, now, CancellationToken.None);
        var dueTargetCount = await repository.CountDueAsync(now, CancellationToken.None);
        var wrongTenantClaims = await repository.ClaimDueAsync(
            new WebhookLocalTargetClaimRequest(
                1,
                10,
                10,
                [tenantB.Id],
                now,
                TimeSpan.FromMinutes(5),
                graphA.Target.Id),
            new Dictionary<Guid, WebhookDeliveryClaimLimits>
            {
                [tenantB.Id] = new(10, 10, 10)
            },
            CancellationToken.None);
        var claim = (await repository.ClaimDueAsync(
            new WebhookLocalTargetClaimRequest(
                1,
                10,
                10,
                [tenantA.Id],
                now,
                TimeSpan.FromMinutes(5),
                graphA.Target.Id),
            new Dictionary<Guid, WebhookDeliveryClaimLimits>
            {
                [tenantA.Id] = new(10, 10, 10)
            },
            CancellationToken.None)).Single();
        var ownedTarget = await repository.GetActiveClaimAsync(
            tenantA.Id,
            graphA.Target.Id,
            claim.LeaseToken,
            claim.DeliveryFence,
            now.AddSeconds(1),
            CancellationToken.None);
        ownedTarget!.MarkSucceeded(
            claim.LeaseToken,
            claim.DeliveryFence,
            now.AddSeconds(1));
        await repository.SaveChangesAsync(CancellationToken.None);

        await using var verifyContext = fixture.CreateDbContext();
        var targets = await verifyContext.WebhookLocalTargetSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(target => target.Id);
        await Assert.That(visibleTargets).IsEquivalentTo([graphB.Target.Id]);
        await Assert.That(dueTenantIds).IsEquivalentTo(new[] { tenantA.Id, tenantB.Id });
        await Assert.That(dueTargetCount).IsEqualTo(2);
        await Assert.That(wrongTenantClaims).IsEmpty();
        await Assert.That(targets[graphA.Target.Id].DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Succeeded);
        await Assert.That(targets[graphB.Target.Id].DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Webhook Bypass {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static WebhookConsumer CreateConsumer(
        Guid tenantId,
        string name,
        WebhookProviderMode providerMode,
        string externalProviderAppId)
    {
        return new WebhookConsumer
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = name,
            Status = WebhookConsumerStatus.Active,
            ProviderMode = providerMode,
            ExternalProviderAppId = externalProviderAppId,
            ConfigurationVersion = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    private static WebhookEventType CreateEventType(string name)
    {
        return new WebhookEventType
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            GroupName = name.Split('.')[0],
            Description = $"Webhook event type for {name}",
            SchemaJson = "{\"type\":\"object\",\"additionalProperties\":true}",
            SchemaVersion = 1,
            IsPublic = true,
            IsEnabled = true,
            PayloadRetentionDays = 14,
        };
    }

    private static WebhookEndpoint CreateEndpoint(
        Guid tenantId,
        Guid consumerId,
        string name,
        WebhookEndpointStatus status)
    {
        return new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ConsumerId = consumerId,
            Url = $"https://example.com/webhooks/{name}",
            Status = status,
            SecretRef = $"webhooks/{name}/secret",
            SecretVersion = 1,
            SecretActivatedAt = new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc),
            ConfigurationVersion = 1,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    private static WebhookEndpointSubscription CreateSubscription(
        Guid tenantId,
        Guid endpointId,
        Guid eventTypeId)
    {
        return new WebhookEndpointSubscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            IsEnabled = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    private static WebhookMessage CreateMessage(
        Guid tenantId,
        Guid consumerId,
        string eventId,
        DateTime? retentionUntil = null)
    {
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return WebhookMessage.Create(
            tenantId,
            "event.published",
            eventId,
            "event",
            Guid.CreateVersion7(),
            consumerId,
            System.Text.Encoding.UTF8.GetBytes($"{{\"id\":\"{eventId}\"}}"),
            "application/json",
            "utf-8",
            createdAt,
            retentionUntil ?? new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            createdAt);
    }

    private static (WebhookDeliveryPlanSnapshot Plan, WebhookLocalTargetSnapshot Target) CreateTargetGraph(
        WebhookMessage message,
        WebhookEndpoint endpoint,
        WebhookConsumer consumer,
        DateTimeOffset capturedAtUtc)
    {
        var plan = WebhookDeliveryPlanSnapshot.Create(
            message.TenantId,
            message.Id,
            consumer.Id,
            WebhookProviderMode.Local,
            $"consumer-v{consumer.ConfigurationVersion}",
            "contract-v1",
            "standard",
            "retention-v1",
            new DateTimeOffset(message.PayloadRetentionUntil),
            capturedAtUtc.AddDays(30),
            capturedAtUtc.AddDays(90),
            capturedAtUtc.AddDays(90),
            capturedAtUtc.AddDays(30),
            capturedAtUtc);
        var target = WebhookLocalTargetSnapshot.Create(
            plan,
            endpoint,
            endpoint.ConfigurationVersion,
            new DateTimeOffset(endpoint.SecretActivatedAt),
            null,
            capturedAtUtc);
        return (plan, target);
    }

    private static WebhookDeliveryAttempt CreateDeliveryAttempt(
        Guid tenantId,
        Guid messageId,
        Guid endpointId,
        WebhookDeliveryAttemptOutcome status,
        DateTime scheduledAt,
        int attemptNumber = 1)
    {
        return new WebhookDeliveryAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            MessageId = messageId,
            EndpointId = endpointId,
            AttemptNumber = attemptNumber,
            Outcome = status,
            ScheduledAt = scheduledAt,
            SentAt = status == WebhookDeliveryAttemptOutcome.Sending ? scheduledAt : null,
            CompletedAt = status is WebhookDeliveryAttemptOutcome.Succeeded or WebhookDeliveryAttemptOutcome.Failed
                ? scheduledAt.AddSeconds(1)
                : null,
            HttpStatusCode = status == WebhookDeliveryAttemptOutcome.Succeeded ? 204 : null,
            FailureCategory = status == WebhookDeliveryAttemptOutcome.Failed ? "server_error" : null,
            DurationMs = status is WebhookDeliveryAttemptOutcome.Succeeded or WebhookDeliveryAttemptOutcome.Failed ? 123 : null,
            NextRetryAt = status == WebhookDeliveryAttemptOutcome.Failed ? scheduledAt.AddMinutes(10) : null,
            CreatedAt = scheduledAt,
        };
    }

    private static IncomingWebhookMessage CreateIncomingMessage(
        Guid tenantId,
        string provider,
        string providerMessageId,
        string idempotencyKey)
    {
        var receivedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var verifiedAt = receivedAt.AddSeconds(1);
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes("{\"decision\":\"accepted\"}");
        var payloadHash = $"sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payloadBytes)).ToLowerInvariant()}";
        return IncomingWebhookMessage.CreateVerified(
            tenantId,
            provider,
            providerMessageId,
            idempotencyKey,
            "decision.created",
            payloadBytes,
            payloadHash,
            "application/json",
            "utf-8",
            $"{{\"svix-id\":\"{providerMessageId}\"}}",
            receivedAt,
            verifiedAt,
            verifiedAt.AddDays(14),
            "webhook-retention-test-v1",
            verifiedAt.AddDays(30),
            verifiedAt.AddDays(90),
            verifiedAt.AddDays(14),
            verifiedAt.AddDays(30));
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
