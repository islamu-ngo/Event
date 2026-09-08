using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookBulkReplayRepositoryTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime ObservedAt =
        new(2026, 7, 14, 16, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PreviewAndSchedule_ClassifyDisjointlyAndReopenOnlyStillEligibleLocalTargets()
    {
        await fixture.ResetAsync();
        Guid tenantId;
        Guid consumerId;
        Guid eligibleTargetId;
        Guid heldTargetId;
        Guid expiredTargetId;
        Guid inactiveTargetId;
        Guid pendingTargetId;
        await using (var setupContext = fixture.CreateDbContext())
        {
            await LookupTableSeeder.SeedAsync(setupContext);
            var tenant = CreateTenant();
            tenantId = tenant.Id;
            setupContext.Tenants.Add(tenant);
            await setupContext.SaveChangesAsync();

            var consumer = CreateConsumer(tenant.Id);
            consumerId = consumer.Id;
            var activeEndpoint = CreateEndpoint(tenant.Id, consumer.Id, "active");
            var inactiveEndpoint = CreateEndpoint(tenant.Id, consumer.Id, "inactive");
            setupContext.AddRange(consumer, activeEndpoint, inactiveEndpoint);
            await setupContext.SaveChangesAsync();

            var eligible = CreateTargetGraph(
                tenant.Id,
                consumer,
                activeEndpoint,
                "eligible",
                ObservedAt.AddDays(7),
                terminal: true);
            var held = CreateTargetGraph(
                tenant.Id,
                consumer,
                activeEndpoint,
                "held",
                ObservedAt.AddDays(7),
                terminal: true);
            var expired = CreateTargetGraph(
                tenant.Id,
                consumer,
                activeEndpoint,
                "expired",
                ObservedAt.AddDays(-1),
                terminal: true);
            var inactive = CreateTargetGraph(
                tenant.Id,
                consumer,
                inactiveEndpoint,
                "inactive",
                ObservedAt.AddDays(7),
                terminal: true);
            var pending = CreateTargetGraph(
                tenant.Id,
                consumer,
                activeEndpoint,
                "pending",
                ObservedAt.AddDays(7),
                terminal: false);
            inactiveEndpoint.Status = WebhookEndpointStatus.Disabled;
            setupContext.AddRange(
                eligible.Message,
                eligible.Plan,
                eligible.Target,
                held.Message,
                held.Plan,
                held.Target,
                expired.Message,
                expired.Plan,
                expired.Target,
                inactive.Message,
                inactive.Plan,
                inactive.Target,
                pending.Message,
                pending.Plan,
                pending.Target,
                WebhookRetentionHold.Create(
                    tenant.Id,
                    WebhookRetentionSubjectKind.OutgoingMessage,
                    held.Message.Id,
                    "legal_hold",
                    ObservedAt.AddDays(-1)));
            await setupContext.SaveChangesAsync();
            eligibleTargetId = eligible.Target.Id;
            heldTargetId = held.Target.Id;
            expiredTargetId = expired.Target.Id;
            inactiveTargetId = inactive.Target.Id;
            pendingTargetId = pending.Target.Id;
        }

        await using (var operationContext = fixture.CreateDbContext())
        {
            var repository = new WebhookBulkReplayRepository(operationContext);
            var filter = new WebhookBulkReplayFilter(
                ObservedAt.AddDays(-14),
                ObservedAt.AddDays(1),
                consumerId,
                null,
                "event.published");
            var preview = await repository.PreviewAsync(
                tenantId,
                filter,
                ObservedAt,
                CancellationToken.None);

            await Assert.That(preview.EligibleCount).IsEqualTo(1);
            await Assert.That(preview.HeldCount).IsEqualTo(1);
            await Assert.That(preview.PayloadUnavailableCount).IsEqualTo(1);
            await Assert.That(preview.EndpointUnavailableCount).IsEqualTo(1);
            await Assert.That(preview.IneligibleLocalStateCount).IsEqualTo(1);
            await Assert.That(preview.TotalExcludedCount).IsEqualTo(4);

            var operation = WebhookBulkReplayOperation.Create(
                tenantId,
                Guid.CreateVersion7(),
                $"sha256:{new string('d', 64)}",
                filter.FromUtc,
                filter.ToUtc,
                filter.WebhookConsumerId,
                filter.WebhookEndpointId,
                filter.EventType,
                10,
                "operator.recovery",
                preview,
                ObservedAt);
            await repository.CreateAsync(operation, CancellationToken.None);
            await new EfCoreUnitOfWork(operationContext).ExecuteInTransactionAsync(async token =>
            {
                operation.Start(ObservedAt.AddSeconds(1));
                var scheduled = await repository.ScheduleEligibleLocalTargetsAsync(
                    operation,
                    ObservedAt.AddSeconds(1),
                    token);
                operation.Complete(scheduled, ObservedAt.AddSeconds(2));
                await repository.UpdateAsync(operation, token);
            }, CancellationToken.None);
            await Assert.That(operation.ScheduledCount).IsEqualTo(1);
        }

        await using var verificationContext = fixture.CreateDbContext();
        var statuses = await verificationContext.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(target => target.TenantId == tenantId)
            .ToDictionaryAsync(target => target.Id, target => target.DeliveryStatusId);
        await Assert.That(statuses[eligibleTargetId]).IsEqualTo((int)WebhookLocalDeliveryStatus.RetryDue);
        await Assert.That(statuses[heldTargetId]).IsEqualTo((int)WebhookLocalDeliveryStatus.DeadLettered);
        await Assert.That(statuses[expiredTargetId]).IsEqualTo((int)WebhookLocalDeliveryStatus.DeadLettered);
        await Assert.That(statuses[inactiveTargetId]).IsEqualTo((int)WebhookLocalDeliveryStatus.DeadLettered);
        await Assert.That(statuses[pendingTargetId]).IsEqualTo((int)WebhookLocalDeliveryStatus.Pending);
    }

    private static Tenant CreateTenant() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FullName = "Webhook Bulk Replay Test",
            Slug = $"webhook-bulk-replay-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };

    private static WebhookConsumer CreateConsumer(Guid tenantId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "Bulk Replay Consumer",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 1,
            CreatedAt = ObservedAt.AddDays(-30)
        };

    private static WebhookEndpoint CreateEndpoint(Guid tenantId, Guid consumerId, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ConsumerId = consumerId,
            Url = $"https://{name}.example.test/webhooks",
            Status = WebhookEndpointStatus.Active,
            SecretRef = $"webhook/{name}/secret",
            SecretVersion = 1,
            SecretActivatedAt = ObservedAt.AddDays(-30),
            ConfigurationVersion = 1,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = ObservedAt.AddDays(-30)
        };

    private static TargetGraph CreateTargetGraph(
        Guid tenantId,
        WebhookConsumer consumer,
        WebhookEndpoint endpoint,
        string identity,
        DateTime retentionUntil,
        bool terminal)
    {
        var materializedAt = ObservedAt.AddDays(-4);
        var message = WebhookMessage.Create(
            tenantId,
            "event.published",
            identity,
            "event",
            Guid.CreateVersion7(),
            consumer.Id,
            System.Text.Encoding.UTF8.GetBytes($"{{\"id\":\"{identity}\"}}"),
            "application/json",
            "utf-8",
            materializedAt,
            retentionUntil,
            materializedAt);
        var capturedAt = new DateTimeOffset(materializedAt);
        var plan = WebhookDeliveryPlanSnapshot.Create(
            tenantId,
            message.Id,
            consumer.Id,
            WebhookProviderMode.Local,
            "consumer-v1",
            "contract-v1",
            "standard",
            "retention-v1",
            new DateTimeOffset(retentionUntil),
            capturedAt.AddDays(30),
            capturedAt.AddDays(90),
            capturedAt.AddDays(90),
            capturedAt.AddDays(30),
            capturedAt);
        var target = WebhookLocalTargetSnapshot.Create(
            plan,
            endpoint,
            endpoint.ConfigurationVersion,
            new DateTimeOffset(endpoint.SecretActivatedAt),
            null,
            capturedAt);
        if (terminal)
        {
            var leaseToken = Guid.CreateVersion7();
            var claimedAt = capturedAt.AddSeconds(1);
            target.ClaimForDelivery(
                "bulk-replay-test-worker",
                leaseToken,
                claimedAt.AddMinutes(1),
                claimedAt);
            target.DeadLetter(
                leaseToken,
                target.DeliveryFence,
                claimedAt.AddSeconds(1));
        }

        return new TargetGraph(message, plan, target);
    }

    private sealed record TargetGraph(
        WebhookMessage Message,
        WebhookDeliveryPlanSnapshot Plan,
        WebhookLocalTargetSnapshot Target);
}
