using Event.Api.IntegrationTests.Builders;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features.Notifications;

[NotInParallel]
public sealed partial class NotificationOutboxWorkerAdmissionTests
{
    [Test]
    public async Task PairedDiscoveryCorrectionUsesFreshReceiptTenantAndDoesNotRetargetOnReplay()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var hosted = factory;
        using var client = hosted.CreateClient();
        Guid tenantId;
        Guid eventId;
        Guid userId;
        Guid messageId;
        await using (var database = factory.CreateDatabase())
        {
            var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
            userId = publisher.UserId;
            var sourceTenant = new TenantBuilder().Build();
            tenantId = sourceTenant.Id;
            database.Tenants.Add(sourceTenant);
            database.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = sourceTenant,
                UserId = userId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active
            });
            var source = new EventBuilder().WithTenantId(tenantId).WithActorId(publisher.ActorId)
                .WithStatus(EventStatusEnum.Published).Build();
            var primary = new EventBuilder().WithTenantId(tenantId).WithActorId(publisher.ActorId)
                .WithStatus(EventStatusEnum.Published).Build();
            database.Events.AddRange(source, primary);
            eventId = source.Id;
            DateTime occurredAt = DateTime.UtcNow;
            database.EventRoleAssignments.Add(
                EventRoleAssignment.Create(tenantId, source.Id, userId, (int)RoleEnum.EventOwner,
                    EventRoleAssignmentStatus.Active, occurredAt, null, userId));
            string payload = JsonSerializer.Serialize(new EventDiscoveryIdentityCorrectionRequested(
                tenantId, eventId, primary.Id, 1, "same-offering", "confirmed_alias"));
            database.Set<AuditLog>().Add(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = sourceTenant,
                ActorId = userId,
                EntityType = "EventDiscoveryIdentity",
                EntityId = eventId.ToString("D"),
                Action = "EventDiscoveryAliasConfirmed",
                NewValues = payload,
                Timestamp = occurredAt
            });
            messageId = Guid.CreateVersion7();
            database.OutboxMessages.Add(new OutboxMessage
            {
                Id = messageId,
                AggregateType = "EventDiscoveryIdentity",
                AggregateId = eventId,
                EventType = EventDiscoveryIdentityCorrectionRequested.EventType,
                Payload = payload,
                Status = OutboxMessageStatus.Pending,
                CreatedAt = occurredAt,
                MaxRetries = 5
            });
            await database.SaveChangesAsync();
        }
        await using var execution = await NotificationOutboxExecutionFixture.ClaimAndBindAsync(hosted.Services, messageId);
        var databaseInScope = execution.Services.GetRequiredService<ExploreDbContext>();
        var tenant = execution.Services.GetRequiredService<ITenantContextAccessor>();
        await Assert.That(tenant.TenantId).IsEqualTo(tenantId);
        var dispatcher = execution.Services.GetRequiredService<IOutboxMessageDispatcher>();
        await dispatcher.DispatchAsync(execution.Message);
        await dispatcher.DispatchAsync(execution.Message);
        await Assert.That(tenant.TenantId).IsEqualTo(tenantId);
        databaseInScope.ChangeTracker.Clear();
        var notifications = await databaseInScope.Notifications.AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.UserId == userId
                && value.EntityId == eventId.ToString())
            .ToListAsync();
        await Assert.That(notifications.Count).IsEqualTo(1);
        await Assert.That(notifications[0].DeduplicationKey)
            .IsEqualTo($"event-discovery-identity:{tenantId:N}:{messageId:N}:{eventId:N}:{userId:N}:in-app");
    }

    [Test]
    [Arguments(EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType)]
    [Arguments(EventModerationOutboxMessageFactory.EventLightModeratedNotificationFanoutRequestedEventType)]
    [Arguments(EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType)]
    [Arguments(NotificationFanoutOccurrenceOutboxMessageFactory.EventType)]
    [Arguments(EventDiscoveryIdentityCorrectionRequested.EventType)]
    public async Task DirectCompositeNotificationRouteRejectsSyntheticClaimBeforeParsing(string eventType)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var hosted = factory;
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "Event",
            AggregateId = Guid.CreateVersion7(),
            EventType = eventType,
            Payload = "{}",
            Status = OutboxMessageStatus.Processing,
            NextRetryAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(message, CancellationToken.None));
        await Assert.That(failure!.Message).IsEqualTo("notification_outbox_execution_not_admitted");
        await Assert.That(message.Payload).IsEqualTo("{}");
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await database.OutboxMessages.AnyAsync(row => row.Id == message.Id)).IsFalse();
        await Assert.That(await database.NotificationFanoutRuns.AnyAsync(row =>
            row.EntityId == message.AggregateId || row.FanoutOccurrenceId == message.AggregateId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeWorker_UsesFreshSourceTenantAndOriginalClaim_ForOccurrenceHandoff(bool replaceClaim)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationOutboxAdmissionDbContextFactory>();
            services.AddSingleton<INotificationOutboxAdmissionDbContextFactory, AdmissionFactory>();
        }));
        using var client = hosted.CreateClient();
        Guid messageId;
        Guid occurrenceId;
        Guid tenantId;
        await using (var scope = hosted.Services.CreateAsyncScope())
        {
            await using var database = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>()
                .CreateDbContextAsync();
            var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
            var foreignTenant = new TenantBuilder().WithId(Guid.CreateVersion7())
                .WithSlug("worker-" + Guid.CreateVersion7().ToString("N")).Build();
            database.Tenants.Add(foreignTenant);
            tenantId = foreignTenant.Id;
            var source = new EventBuilder().WithTenantId(tenantId).WithActorId(publisher.OrganizationActorId)
                .WithStatus(EventStatusEnum.Published).Build();
            database.Events.Add(source);
            var now = DateTime.UtcNow;
            var occurrence = NotificationFanoutOccurrence.Create(
                Guid.CreateVersion7(), tenantId, source.Id, null, now, now,
                Guid.CreateVersion7(), "[]", "{}", "{}", "event_changed", 1,
                (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired, 1, 100,
                now, "event", source.Id, "worker-admission", null, 0);
            occurrenceId = occurrence.Id;
            database.NotificationFanoutOccurrences.Add(occurrence);
            var message = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence);
            messageId = message.Id;
            database.OutboxMessages.Add(message);
            await database.SaveChangesAsync();
        }

        await using var admissionScope = hosted.Services.CreateAsyncScope();
        var claim = (await admissionScope.ServiceProvider.GetRequiredService<INotificationOutboxAdmission>()
            .ClaimDueAsync(1000, DateTime.UtcNow, CancellationToken.None)).Single(row => row.MessageId == messageId);

        // Process the real admitted claim through the production worker and Composite dispatcher.
        var processor = new OutboxProcessor(hosted.Services, Options.Create(new OutboxProcessorSettings()),
            NullLogger<OutboxProcessor>.Instance);
        var genericRepository = admissionScope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var genericMessage = await genericRepository.GetClaimedNotificationAsync(messageId, claim.ProcessingLeaseExpiresAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessGenericClaimAsync(
            new GenericOutboxClaim(claim.MessageId, claim.TenantId, GenericOutboxOwnership.Tenant,
                claim.SourceId, claim.SourceId.ToString("D"), claim.AggregateId, claim.AggregateType,
                claim.EventType, claim.PayloadDigest, genericMessage!.CreatedAt,
                OutboxMessageStatus.Processing, claim.ProcessingLeaseExpiresAt), CancellationToken.None));
        await using var directScope = hosted.Services.CreateAsyncScope();
        directScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
        await directScope.ServiceProvider.GetRequiredService<INotificationOutboxExecutionAuthority>()
            .BindAsync(claim, CancellationToken.None);
        var original = await directScope.ServiceProvider.GetRequiredService<IOutboxRepository>()
            .GetClaimedNotificationAsync(messageId, claim.ProcessingLeaseExpiresAt);
        await Assert.That(original).IsNotNull();
        await Assert.That(original!.NextRetryAt!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        DateTime? replacementLease = null;
        if (replaceClaim)
        {
            replacementLease = await admissionScope.ServiceProvider.GetRequiredService<IOutboxRepository>()
                .TryClaimForProcessing(messageId, claim.ProcessingLeaseExpiresAt);
            await Assert.That(replacementLease).IsNotNull();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessNotificationClaimAsync(claim, CancellationToken.None));
        }
        else
            await processor.ProcessNotificationClaimAsync(claim, CancellationToken.None);

        // The original bound scope cannot bypass a replaced or completed claim through Composite.
        var directFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            directScope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>()
                .DispatchAsync(original!, CancellationToken.None));
        await Assert.That(directFailure!.Message).IsEqualTo("notification_outbox_execution_not_admitted");

        await using var verifyScope = hosted.Services.CreateAsyncScope();
        verifyScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
        var verify = verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var row = await verify.OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == messageId);
        if (replaceClaim)
        {
            await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.Processing);
            await Assert.That(row.NextRetryAt).IsEqualTo(replacementLease);
            await Assert.That(await verify.NotificationFanoutRuns.AnyAsync(item =>
                item.FanoutOccurrenceId == occurrenceId)).IsFalse();
        }
        else
        {
            await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.Completed);
            await Assert.That(row.NextRetryAt).IsNull();
            var run = await verify.NotificationFanoutRuns.AsNoTracking()
                .SingleAsync(item => item.FanoutOccurrenceId == occurrenceId);
            await Assert.That(run.TenantId).IsEqualTo(tenantId);
            await Assert.That(run.Status).IsEqualTo("pending");
        }
    }

    private sealed class AdmissionFactory(IDbContextFactory<ExploreDbContext> factory)
        : INotificationOutboxAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }
}
