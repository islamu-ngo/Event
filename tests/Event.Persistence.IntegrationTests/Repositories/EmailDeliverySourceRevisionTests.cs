// ABOUTME: Verifies immutable fanout source revisions across real SQLite email-policy changes.
// ABOUTME: Protects coalescing boundaries, historical replay, and pretransaction lock ownership.

using Explore.Application.Models.InternalEvents;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliverySourceRevisionTests
{
    [Test]
    public Task EnabledUpdate_DoesNotCoalesceDisabledHistory() =>
        WithScenarioAsync(async (context, tenantId, eventId) =>
        {
            await SetEnabledAsync(context, tenantId, enabled: false);
            DateTime at = DateTime.UtcNow;
            var disabledCandidate = Candidate(tenantId, eventId, at, startingHour: 9);
            var enabledCandidate = Candidate(tenantId, eventId, at.AddMinutes(1), startingHour: 10);
            var disabled = await CoordinateAsync(context, disabledCandidate);

            await SetEnabledAsync(context, tenantId, enabled: true);
            var enabled = await CoordinateAsync(context, enabledCandidate);
            var persistedDisabled = await ReadOccurrenceAsync(context, disabledCandidate);

            await Assert.That(enabled.Occurrence.SafeBeforeSnapshotJson).IsEqualTo(enabledCandidate.SafeBeforeSnapshotJson);
            await Assert.That(disabled.Occurrence.EmailDeliveryPolicyRevision).IsEqualTo(1L);
            await Assert.That(enabled.Occurrence.EmailDeliveryPolicyRevision).IsEqualTo(2L);
            await Assert.That(persistedDisabled!.State).IsEqualTo(NotificationFanoutOccurrenceState.Superseded);
            await Assert.That(persistedDisabled.SupersededByOccurrenceId).IsEqualTo(enabled.Occurrence.Id);
            await Assert.That(persistedDisabled.EmailDeliveryPolicyRevision).IsEqualTo(1L);
        });

    [Test]
    public Task CoalescedSourceReplay_AfterPolicyChangeRetainsOriginalRevisionAndContent() =>
        WithScenarioAsync(async (context, tenantId, eventId) =>
        {
            await SetEnabledAsync(context, tenantId, enabled: true);
            DateTime at = DateTime.UtcNow;
            var firstCandidate = Candidate(tenantId, eventId, at, startingHour: 9);
            var secondCandidate = Candidate(tenantId, eventId, at.AddMinutes(1), startingHour: 10);
            await CoordinateAsync(context, firstCandidate);
            var original = await CoordinateAsync(context, secondCandidate);
            await Assert.That(original.Occurrence.SafeBeforeSnapshotJson).IsEqualTo(firstCandidate.SafeBeforeSnapshotJson);

            await SetEnabledAsync(context, tenantId, enabled: false);
            await SetEnabledAsync(context, tenantId, enabled: true);
            var replay = await CoordinateAsync(context, secondCandidate);
            var control = await new EmailDispatchOutboxRepository(context).GetTenantControl(tenantId, CancellationToken.None);
            var persisted = await ReadOccurrenceAsync(context, secondCandidate);

            await Assert.That(control!.DeliveryPolicyRevision).IsEqualTo(3L);
            await Assert.That(replay.Outcome).IsEqualTo(NotificationFanoutOccurrenceCoordinationOutcome.SourceReplay);
            await Assert.That(replay.PointerCreated).IsFalse();
            await Assert.That(replay.Occurrence.EmailDeliveryPolicyRevision).IsEqualTo(1L);
            await Assert.That(persisted!.EmailDeliveryPolicyRevision).IsEqualTo(1L);
            await Assert.That(replay.Occurrence.SafeBeforeSnapshotJson).IsEqualTo(firstCandidate.SafeBeforeSnapshotJson);
            await Assert.That(replay.Occurrence.SafeAfterSnapshotJson).IsEqualTo(secondCandidate.SafeAfterSnapshotJson);
        });

    [Test]
    public Task MissingOuterPolicyOwnership_FailsBeforeSourcePersistence() =>
        WithScenarioAsync(async (context, tenantId, eventId) =>
        {
            var candidate = Candidate(tenantId, eventId, DateTime.UtcNow, startingHour: 9);
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var coordinator = CreateCoordinator(context, mutationLock);

            await Assert.That(() => unitOfWork.ExecuteInTransactionAsync(token =>
                    coordinator.CoordinateInCurrentTransactionAsync(candidate, token)))
                .Throws<InvalidOperationException>();

            await Assert.That(await ReadOccurrenceAsync(context, candidate)).IsNull();
        });

    private static Task<NotificationFanoutOccurrenceCoordinationResult> CoordinateAsync(
        ExploreDbContext context,
        NotificationFanoutOccurrenceCandidate candidate)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var coordinator = CreateCoordinator(context, mutationLock);
        return mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            outerToken => unitOfWork.ExecuteInTransactionAsync(
                token => coordinator.CoordinateInCurrentTransactionAsync(candidate, token), outerToken));
    }

    private static NotificationFanoutOccurrenceCoordinator CreateCoordinator(
        ExploreDbContext context,
        RelationalSettingMutationLock mutationLock) =>
        new(new NotificationFanoutOccurrenceRepository(context),
            new NotificationFanoutEmailSuppressionRepository(context),
            new OutboxRepository(context),
            new NotificationFanoutRecipientTemplateFactory(),
            new EmailDispatchOutboxRepository(context),
            mutationLock);

    private static Task<NotificationFanoutOccurrence?> ReadOccurrenceAsync(
        ExploreDbContext context,
        NotificationFanoutOccurrenceCandidate candidate) =>
        new NotificationFanoutOccurrenceRepository(context).GetByPointerAsync(
            new NotificationFanoutOccurrenceRequested(candidate.TenantId, candidate.OccurrenceId,
                NotificationFanoutOccurrenceRequested.CurrentVersion));

    private static Task SetEnabledAsync(ExploreDbContext context, Guid tenantId, bool enabled) =>
        enabled
            ? SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.DeliveryEnabled,
                value: "true", tenantId: tenantId)
            : ConfirmEmailDisableAsync(context: context, tenantId: tenantId);

    private static NotificationFanoutOccurrenceCandidate Candidate(
        Guid tenantId,
        Guid eventId,
        DateTime occurredAt,
        int startingHour) =>
        new(OccurrenceId: Guid.CreateVersion7(), PointerOutboxMessageId: Guid.CreateVersion7(),
            TenantId: tenantId, EventId: eventId, SessionId: null,
            OccurredAt: occurredAt, AudienceCutoffAt: occurredAt, AggregateVersion: Guid.CreateVersion7(),
            ChangeSetJson: NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1([
                NotificationFanoutChangeField.StartTime])),
            SafeBeforeSnapshotJson: Snapshot(startingHour), SafeAfterSnapshotJson: Snapshot(startingHour + 1),
            TemplateKey: NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            TemplateVersion: NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
            DeliveryPolicyId: (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            PolicyVersion: NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
            RequestedNotBefore: occurredAt, SourceType: "event-mutation", SourceId: Guid.CreateVersion7());

    private static string Snapshot(int hour) =>
        NotificationFanoutTemplateJson.Serialize(new NotificationFanoutSnapshotV1(
            "Revision event", null, new DateTimeOffset(2030, 1, 1, hour, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 2, 12, 0, 0, TimeSpan.Zero), "UTC", null));

    private static async Task WithScenarioAsync(Func<ExploreDbContext, Guid, Guid, Task> test)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-source-revision-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
            await SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
                value: "false", mutationLock: mutationLock);
            var seeded = await SeedProcessingDispatchAsync(context, "source-revision");
            var principal = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(), Code = $"source-revision-{Guid.CreateVersion7():N}",
                DisplayName = "Source revision worker", ConcurrencyStamp = Guid.CreateVersion7()
            };
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.Bot, ActorType = null!,
                ServicePrincipalId = principal.Id, ServicePrincipal = principal,
                Pii = new ActorPii { DisplayName = "Source revision worker" }, ConcurrencyStamp = Guid.CreateVersion7()
            };
            var @event = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(), TenantId = seeded.TenantId, Tenant = null!,
                Title = "Revision event", EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, ConcurrencyStamp = Guid.CreateVersion7()
            };
            context.AddRange(actor, @event);
            await context.SaveChangesAsync();
            await test(context, seeded.TenantId, @event.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }
    }
}
