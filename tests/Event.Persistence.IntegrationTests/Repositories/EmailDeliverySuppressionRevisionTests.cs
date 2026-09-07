// ABOUTME: Verifies optional email suppression at final admission using exact-tenant source revisions.
// ABOUTME: Uses misleading timestamps to prove clock-independent ordering, tenant isolation, and untouched handoff evidence.

using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema.ProviderPrimitives;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliverySuppressionRevisionTests
{
    private const long SuppressionRevision = 1;
    private static readonly DateTime AuditCutoff = new(2026, 8, 1, 12, 0, 0, 500, DateTimeKind.Utc);

    [Test]
    [Arguments(0L, true)]
    [Arguments(1L, true)]
    [Arguments(2L, false)]
    public async Task OptionalOccurrence_UsesInclusiveExactTenantRevision(long occurrenceRevision, bool suppressed)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, occurrenceRevision: occurrenceRevision, ownCutoff: SuppressionRevision);
            await AssertAdmissionAsync(databasePath, scenario, suppressed);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NullTenantWatermark_DoesNotInheritGlobalOrOtherTenantHistory(bool otherTenantHistory)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, occurrenceRevision: 0, ownCutoff: null,
                globalCutoff: otherTenantHistory ? null : SuppressionRevision, otherTenantHistory: otherTenantHistory);
            await AssertAdmissionAsync(databasePath, scenario, suppressed: false);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task DelayedFanout_UsesOriginalSourceRevisionInsteadOfNewGraphRevision()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, occurrenceRevision: 0, ownCutoff: SuppressionRevision, fanout: true);
            await AssertAdmissionAsync(databasePath, scenario, suppressed: true);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task RequiredModerationAvailability_IsNotSuppressedByOptionalHistory()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, occurrenceRevision: 0, ownCutoff: SuppressionRevision,
                fanout: true, required: true);
            await AssertAdmissionAsync(databasePath, scenario, suppressed: false);
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static async Task<Scenario> SeedAsync(string databasePath, long occurrenceRevision, long? ownCutoff,
        long? globalCutoff = null, bool otherTenantHistory = false, bool fanout = false, bool required = false)
    {
        await CreateDatabaseAsync(databasePath);
        await using var context = CreateContext(databasePath);
        var seeded = await SeedProcessingDispatchAsync(context, $"cutoff-{Guid.CreateVersion7():N}");
        var dispatch = await context.EmailDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .Include(row => row.NotificationIntent).ThenInclude(intent => intent!.Deliveries)
            .SingleAsync(row => row.TenantId == seeded.TenantId && row.Id == seeded.OutboxId);
        var intent = dispatch.NotificationIntent!;
        var delivery = intent.Deliveries.Single();
        // Deliberately reverse clock order: old sources appear newer, new sources appear older.
        DateTime originalAt = AuditCutoff.AddDays(occurrenceRevision > SuppressionRevision ? -1 : 1);
        dispatch.CreatedAt = originalAt;
        intent.CreatedAt = originalAt;
        intent.EmailDeliveryPolicyRevision = fanout ? SuppressionRevision + 1 : occurrenceRevision;
        delivery.CreatedAt = intent.CreatedAt;
        delivery.QueuedAt = intent.CreatedAt;
        if (fanout)
            await LinkFanoutAsync(context, dispatch, delivery, originalAt, occurrenceRevision, required);

        DateTime refillAt = (await RelationalDatabaseClock.GetUtcNowAsync(context, CancellationToken.None)).AddDays(1);
        var processor = await context.EmailDispatchProcessorStates
            .SingleAsync(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode);
        processor.OptionalSuppressedThroughRevision = globalCutoff;
        processor.OptionalSuppressedThroughUtc = globalCutoff.HasValue ? AuditCutoff : null;
        processor.SmtpAvailableTokens = 7;
        processor.SmtpRefillAt = refillAt;
        processor.IsPaused = false;
        context.EmailDispatchTenantControls.Add(new EmailDispatchTenantControl
        {
            Id = Guid.CreateVersion7(), TenantId = seeded.TenantId, DeliveryPolicyRevision = 2,
            OptionalSuppressedThroughRevision = ownCutoff, OptionalSuppressedThroughUtc = ownCutoff.HasValue ? AuditCutoff : null,
            SmtpAvailableTokens = 5, SmtpRefillAt = refillAt,
            CreatedAt = originalAt
        });
        if (otherTenantHistory)
        {
            var otherTenant = new Tenant
            {
                Id = Guid.CreateVersion7(), FullName = "Other suppression tenant", Slug = $"cutoff-other-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
            };
            context.Tenants.Add(otherTenant);
            context.EmailDispatchTenantControls.Add(new EmailDispatchTenantControl
            {
                Id = Guid.CreateVersion7(), TenantId = otherTenant.Id, DeliveryPolicyRevision = 2,
                OptionalSuppressedThroughRevision = SuppressionRevision, OptionalSuppressedThroughUtc = AuditCutoff, CreatedAt = originalAt
            });
        }
        await context.SaveChangesAsync();
        return new(Dispatch: seeded, RefillAt: refillAt);
    }

    private static async Task LinkFanoutAsync(ExploreDbContext context, EmailDispatchOutbox dispatch,
        NotificationDelivery delivery, DateTime originalAt, long occurrenceRevision, bool required)
    {
        var principal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(), Code = $"cutoff-worker-{Guid.CreateVersion7():N}",
            DisplayName = "Cutoff fanout worker", ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.Bot, ActorType = null!,
            ServicePrincipalId = principal.Id, ServicePrincipal = principal,
            Pii = new ActorPii { DisplayName = "Cutoff fanout worker" }, ConcurrencyStamp = Guid.CreateVersion7()
        };
        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), TenantId = dispatch.TenantId, Tenant = null!, Title = "Suppression cutoff event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, ConcurrencyStamp = Guid.CreateVersion7()
        };
        string template = required
            ? NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey : "event_update";
        int policy = (int)(required ? NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired
            : NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional);
        var occurrence = NotificationFanoutOccurrence.Create(
            id: Guid.CreateVersion7(), tenantId: dispatch.TenantId, eventId: @event.Id, sessionId: null,
            occurredAt: originalAt, audienceCutoffAt: originalAt, aggregateVersion: Guid.CreateVersion7(),
            changeSetJson: "{}", safeBeforeSnapshotJson: "{}", safeAfterSnapshotJson: "{}",
            templateKey: template, templateVersion: 1, deliveryPolicyId: policy, policyVersion: 1,
            priority: 1, notBefore: originalAt, sourceType: "event", sourceId: @event.Id,
            coalescingKey: $"cutoff:{@event.Id:N}", coalescingWindowEndsAt: null, emailDeliveryPolicyRevision: occurrenceRevision);
        context.AddRange(actor, @event, occurrence);
        var intent = dispatch.NotificationIntent!;
        intent.FanoutOccurrenceId = occurrence.Id;
        intent.FanoutOccurrence = occurrence;
        intent.EventId = @event.Id;
        intent.Event = @event;
        intent.TemplateKey = template;
        intent.CategoryId = (int)(required ? NotificationCategoryEnum.TrustSafetyModeration : NotificationCategoryEnum.EventLifecycle);
        delivery.DeliveryPolicyId = policy;
        delivery.IsRequired = required;
        delivery.TemplateKey = template;
        delivery.PreferenceCategoryCode = required ? NotificationPreferenceCategoryCodes.TrustSafety : NotificationPreferenceCategoryCodes.EventUpdates;
        dispatch.EventId = @event.Id;
        dispatch.SourceType = "notification_fanout_occurrence";
        dispatch.SourceId = occurrence.Id;
        dispatch.Kind = required ? EmailDispatchKind.ModerationAvailabilityRequired : EmailDispatchKind.EventUpdated;
        await context.SaveChangesAsync();
    }

    private static async Task AssertAdmissionAsync(string databasePath, Scenario scenario, bool suppressed)
    {
        var dispatch = scenario.Dispatch;
        EmailDispatchEligibilityResult result;
        await using (var context = CreateContext(databasePath))
        {
            result = await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(new EmailDispatchEligibilityRequest(
                TenantId: dispatch.TenantId, OutboxId: dispatch.OutboxId, ProcessingLeaseToken: dispatch.LeaseToken,
                AttemptNumber: 0, GlobalSmtpRateLimitPerMinute: 10, TenantSmtpRateLimitPerMinute: 10,
                ConsumerId: "suppression-cutoff-test", EvaluatedAt: DateTime.UtcNow));
        }

        await Assert.That(result.Outcome).IsEqualTo(suppressed ? EmailDispatchEligibilityOutcome.Skipped : EmailDispatchEligibilityOutcome.Eligible);
        await using var observer = CreateContext(databasePath);
        var repository = new EmailDispatchOutboxRepository(observer);
        var persisted = (await repository.GetByTenantAndId(dispatch.TenantId, dispatch.OutboxId, CancellationToken.None))!;
        var delivery = await observer.NotificationDeliveries
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue).AsNoTracking()
            .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
        int attempts = await observer.EmailDispatchAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .CountAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
        int receipts = await observer.EmailDispatchReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .CountAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
        var processor = await observer.EmailDispatchProcessorStates.AsNoTracking()
            .SingleAsync(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode);
        var tenantControl = await observer.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue).AsNoTracking()
            .SingleAsync(row => row.TenantId == dispatch.TenantId);
        await Assert.That(persisted.AttemptCount).IsEqualTo(suppressed ? 0 : 1);
        await Assert.That(attempts).IsEqualTo(suppressed ? 0 : 1);
        await Assert.That(receipts).IsEqualTo(suppressed ? 0 : 1);
        await Assert.That(processor.SmtpAvailableTokens).IsEqualTo(suppressed ? 7 : 6);
        await Assert.That(tenantControl.SmtpAvailableTokens).IsEqualTo(suppressed ? 5 : 4);
        await Assert.That(processor.SmtpRefillAt).IsEqualTo(scenario.RefillAt);
        await Assert.That(tenantControl.SmtpRefillAt).IsEqualTo(scenario.RefillAt);
        if (suppressed)
        {
            await Assert.That(result.ReceiptId).IsNull();
            await Assert.That(result.AttemptNumber).IsNull();
            await Assert.That(result.RecipientEmail).IsNull();
            await Assert.That(string.IsNullOrWhiteSpace(result.SkipReason)).IsFalse();
            await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Skipped);
            await Assert.That(persisted.ProcessingLeaseToken).IsNull();
            await Assert.That(persisted.ProcessingStartedAt).IsNull();
            await Assert.That(persisted.NextAttemptAt).IsNull();
            await Assert.That(persisted.LastFailureCategory).IsEqualTo(result.SkipReason);
            await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
            await Assert.That(delivery.FailureCategory).IsEqualTo(result.SkipReason);
            var claimed = await repository.TryClaimSpecificAsync(new EmailDispatchSpecificClaimRequest(
                TenantId: dispatch.TenantId, PublishEventId: persisted.PublishEventId, LeaseToken: Guid.CreateVersion7(),
                GlobalProcessingLimit: 10, TenantProcessingLimit: 5, OptionalReminderBacklogHighWatermark: 100,
                OptionalReminderBacklogLowWatermark: 50, ClaimedAt: DateTime.UtcNow), CancellationToken.None);
            await Assert.That(claimed).IsNull();
        }
        else
        {
            await Assert.That(result.ReceiptId).IsNotNull();
            await Assert.That(result.AttemptNumber).IsEqualTo(1);
        }
    }

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"email-suppression-cutoff-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }

    private sealed record Scenario(SeededDispatch Dispatch, DateTime RefillAt);
}
