using System.Diagnostics;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class NotificationFanoutOccurrenceRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(EmailDispatchStatus.Pending, false, true)]
    [Arguments(EmailDispatchStatus.RetryScheduled, false, true)]
    [Arguments(EmailDispatchStatus.Processing, false, true)]
    [Arguments(EmailDispatchStatus.Processing, true, false)]
    [Arguments(EmailDispatchStatus.Sent, false, false)]
    [Arguments(EmailDispatchStatus.Unknown, false, false)]
    [Arguments(EmailDispatchStatus.DeadLettered, false, false)]
    [Arguments(EmailDispatchStatus.Parked, false, false)]
    [Arguments(EmailDispatchStatus.Skipped, false, false)]
    public async Task OccurrenceSuppressionPreservesProviderAndTerminalEvidence(
        EmailDispatchStatus status,
        bool providerFenced,
        bool expectedSuppressed)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, $"suppression-{status}-{providerFenced}", includeRecipient: true);
        NotificationFanoutOccurrence occurrence = CreateOccurrence(scenario.TenantId, scenario.EventId);
        context.NotificationFanoutOccurrences.Add(occurrence);
        await context.SaveChangesAsync();
        SuppressionGraph graph = await CreateSuppressionGraphAsync(context, scenario, occurrence, status, providerFenced);
        var occurrenceRepository = new NotificationFanoutOccurrenceRepository(context);
        var repository = new NotificationFanoutEmailSuppressionRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        DateTime suppressedAt = AtPostgresPrecision(DateTime.UtcNow);

        NotificationFanoutEmailSuppressionResult first = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await occurrenceRepository.AcquireSourceThenEventCoordinationLocksAsync(
                scenario.TenantId,
                occurrence.SourceType,
                occurrence.SourceId,
                occurrence.AggregateVersion,
                scenario.EventId,
                token);
            return await repository.SuppressPreHandoffAsync(scenario.TenantId, occurrence.Id, suppressedAt, token);
        });
        NotificationFanoutEmailSuppressionResult replay = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await occurrenceRepository.AcquireSourceThenEventCoordinationLocksAsync(
                scenario.TenantId,
                occurrence.SourceType,
                occurrence.SourceId,
                occurrence.AggregateVersion,
                scenario.EventId,
                token);
            return await repository.SuppressPreHandoffAsync(scenario.TenantId, occurrence.Id, suppressedAt, token);
        });

        context.ChangeTracker.Clear();
        EmailDispatchOutbox dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.DispatchId);
        NotificationDelivery delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.DeliveryId);
        Notification notification = await context.Notifications.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.NotificationId);
        NotificationDelivery inAppDelivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.InAppDeliveryId);
        EmailDispatchAttempt[] attempts = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(value => value.EmailDispatchOutboxId == graph.DispatchId)
            .OrderBy(value => value.AttemptNumber)
            .ToArrayAsync();
        EmailDispatchReceipt[] receipts = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(value => value.EmailDispatchOutboxId == graph.DispatchId)
            .ToArrayAsync();

        await Assert.That(first.OutboxRowsSkipped).IsEqualTo(expectedSuppressed ? 1 : 0);
        await Assert.That(first.DeliveryRowsSuperseded).IsEqualTo(expectedSuppressed ? 1 : 0);
        await Assert.That(first.NotificationsSuppressed).IsEqualTo(1);
        await Assert.That(first.InAppDeliveryRowsSuperseded).IsEqualTo(0);
        await Assert.That(replay).IsEqualTo(new NotificationFanoutEmailSuppressionResult(0, 0));
        await Assert.That(dispatch.Status).IsEqualTo(expectedSuppressed ? EmailDispatchStatus.Skipped : status);
        await Assert.That(dispatch.AttemptCount).IsEqualTo(graph.AttemptCount);
        await Assert.That(delivery.StatusId).IsEqualTo(expectedSuppressed
            ? (int)NotificationDeliveryStatusEnum.Superseded
            : graph.DeliveryStatusId);
        await Assert.That(notification.IsDeleted).IsTrue();
        await Assert.That(notification.DeletedAt).IsEqualTo(suppressedAt);
        await Assert.That(inAppDelivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
        await Assert.That(inAppDelivery.CompletedAt).IsEqualTo(graph.InAppDeliveryCompletedAt);
        await Assert.That(attempts.Select(value => (value.AttemptNumber, value.Outcome, value.FailureCategory)))
            .IsEquivalentTo(graph.Attempts);
        await Assert.That(receipts.Select(value => (value.Status, value.FailureCode)))
            .IsEquivalentTo(graph.Receipts);
    }

    [Test]
    public async Task OccurrenceSuppression_SupersedesPendingInAppDeliveryWhileHidingNotification()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "suppression-pending-in-app", includeRecipient: true);
        NotificationFanoutOccurrence occurrence = CreateOccurrence(scenario.TenantId, scenario.EventId);
        context.NotificationFanoutOccurrences.Add(occurrence);
        await context.SaveChangesAsync();
        SuppressionGraph graph = await CreateSuppressionGraphAsync(
            context,
            scenario,
            occurrence,
            EmailDispatchStatus.Pending,
            providerFenced: false,
            inAppStatus: NotificationDeliveryStatusEnum.Pending);
        var repository = new NotificationFanoutEmailSuppressionRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        DateTime suppressedAt = AtPostgresPrecision(DateTime.UtcNow);

        NotificationFanoutEmailSuppressionResult result = await unitOfWork.ExecuteInTransactionAsync(token =>
            repository.SuppressPreHandoffAsync(scenario.TenantId, occurrence.Id, suppressedAt, token));

        context.ChangeTracker.Clear();
        Notification notification = await context.Notifications.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.NotificationId);
        NotificationDelivery delivery = await context.NotificationDeliveries.AsNoTracking()
            .SingleAsync(value => value.Id == graph.InAppDeliveryId);
        await Assert.That(result.NotificationsSuppressed).IsEqualTo(1);
        await Assert.That(result.InAppDeliveryRowsSuperseded).IsEqualTo(1);
        await Assert.That(notification.IsDeleted).IsTrue();
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Superseded);
        await Assert.That(delivery.CompletedAt).IsEqualTo(suppressedAt);
    }

    [Test]
    public async Task SupersededOccurrenceEligibilitySkipsTransportButKeepsDeliverySuperseded()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "eligibility-superseded", includeRecipient: true);
        NotificationFanoutOccurrence oldOccurrence = CreateOccurrence(scenario.TenantId, scenario.EventId);
        NotificationFanoutOccurrence replacement = CreateOccurrence(
            scenario.TenantId,
            scenario.EventId,
            Guid.CreateVersion7());
        oldOccurrence.Supersede(replacement.Id, "superseded_by_newer_update", DateTime.UtcNow);
        context.NotificationFanoutOccurrences.AddRange(oldOccurrence, replacement);
        await context.SaveChangesAsync();
        SuppressionGraph graph = await CreateSuppressionGraphAsync(
            context,
            scenario,
            oldOccurrence,
            EmailDispatchStatus.Pending,
            providerFenced: false);
        var outboxRepository = new EmailDispatchOutboxRepository(context);
        Guid leaseToken = Guid.CreateVersion7();
        EmailDispatchOutbox? claimed = await outboxRepository.TryClaimSpecificAsync(
            new EmailDispatchSpecificClaimRequest(
                scenario.TenantId,
                graph.PublishEventId,
                leaseToken,
                20,
                5,
                100,
                50,
                DateTime.UtcNow),
            CancellationToken.None);
        await Assert.That(claimed).IsNotNull();
        context.ChangeTracker.Clear();

        EmailDispatchEligibilityResult result = await CreateEligibilityEvaluator(context)
            .EvaluateAndBeginProviderHandoffAsync(
                new EmailDispatchEligibilityRequest(
                    scenario.TenantId,
                    graph.DispatchId,
                    leaseToken,
                    0,
                    120,
                    30,
                    "test-worker",
                    DateTime.UtcNow),
                CancellationToken.None);

        context.ChangeTracker.Clear();
        EmailDispatchOutbox dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.DispatchId);
        NotificationDelivery delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.DeliveryId);
        EmailDispatchAttempt attempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.EmailDispatchOutboxId == graph.DispatchId);
        EmailDispatchReceipt receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.EmailDispatchOutboxId == graph.DispatchId);
        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo(NotificationFanoutEmailSuppressionReason.Code);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Superseded);
        await Assert.That(delivery.ProviderStatus).IsEqualTo(NotificationFanoutEmailSuppressionReason.ProviderStatus);
        await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Skipped);
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Skipped);
    }

    [Test]
    public async Task EligibilityFailsClosedWhenIntentEventDoesNotOwnTheFanoutOccurrence()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "eligibility-authority", includeRecipient: true);
        Guid otherEventId = await CreateEventAsync(context, scenario.TenantId, "Mismatched authority event");
        NotificationFanoutOccurrence occurrence = CreateOccurrence(scenario.TenantId, scenario.EventId);
        context.NotificationFanoutOccurrences.Add(occurrence);
        await context.SaveChangesAsync();
        SuppressionGraph graph = await CreateSuppressionGraphAsync(
            context,
            scenario,
            occurrence,
            EmailDispatchStatus.Pending,
            providerFenced: false);
        NotificationIntent intent = await context.NotificationIntents
            .SingleAsync(value => value.Id == graph.NotificationIntentId);
        intent.EventId = otherEventId;
        await context.SaveChangesAsync();
        var outboxRepository = new EmailDispatchOutboxRepository(context);
        Guid leaseToken = Guid.CreateVersion7();
        await Assert.That(await outboxRepository.TryClaimSpecificAsync(
            new EmailDispatchSpecificClaimRequest(
                scenario.TenantId,
                graph.PublishEventId,
                leaseToken,
                20,
                5,
                100,
                50,
                DateTime.UtcNow),
            CancellationToken.None)).IsNotNull();
        context.ChangeTracker.Clear();

        EmailDispatchEligibilityResult result = await CreateEligibilityEvaluator(context)
            .EvaluateAndBeginProviderHandoffAsync(
                new EmailDispatchEligibilityRequest(
                    scenario.TenantId,
                    graph.DispatchId,
                    leaseToken,
                    0,
                    120,
                    30,
                    "test-worker",
                    DateTime.UtcNow),
                CancellationToken.None);

        NotificationDelivery delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.DeliveryId);
        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("fanout_occurrence_authority_missing");
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
    }

    [Test]
    public async Task EligibilityWithWrongTenantCannotChangeFanoutDelivery()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "eligibility-tenant", includeRecipient: true);
        NotificationFanoutOccurrence occurrence = CreateOccurrence(scenario.TenantId, scenario.EventId);
        context.NotificationFanoutOccurrences.Add(occurrence);
        await context.SaveChangesAsync();
        SuppressionGraph graph = await CreateSuppressionGraphAsync(
            context,
            scenario,
            occurrence,
            EmailDispatchStatus.Pending,
            providerFenced: false);
        var outboxRepository = new EmailDispatchOutboxRepository(context);
        Guid leaseToken = Guid.CreateVersion7();
        await Assert.That(await outboxRepository.TryClaimSpecificAsync(
            new EmailDispatchSpecificClaimRequest(
                scenario.TenantId,
                graph.PublishEventId,
                leaseToken,
                20,
                5,
                100,
                50,
                DateTime.UtcNow),
            CancellationToken.None)).IsNotNull();
        context.ChangeTracker.Clear();

        EmailDispatchEligibilityResult result = await CreateEligibilityEvaluator(context)
            .EvaluateAndBeginProviderHandoffAsync(
                new EmailDispatchEligibilityRequest(
                    Guid.CreateVersion7(),
                    graph.DispatchId,
                    leaseToken,
                    0,
                    120,
                    30,
                    "test-worker",
                    DateTime.UtcNow),
                CancellationToken.None);

        EmailDispatchOutbox dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.DispatchId);
        NotificationDelivery delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == graph.DeliveryId);
        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.LostClaim);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Processing);
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
    }

    [Test]
    public async Task CreateAndLoad_PersistsOccurrenceAndTenantScopedPointer()
    {
        await fixture.ResetAsync();
        var baseline = new PersistenceQueryBaselineInterceptor();
        await using var context = fixture.CreateDbContext(baseline);
        Scenario scenario = await CreateScenarioAsync(context, "occurrence-load");
        var occurrence = CreateOccurrence(scenario.TenantId, scenario.EventId);
        OutboxMessage pointer = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence);
        var repository = new NotificationFanoutOccurrenceRepository(context);
        var outboxRepository = new OutboxRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);

        await unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            await repository.Create(occurrence);
            await outboxRepository.Create(pointer);
        });

        baseline.Reset();
        var contract = NotificationFanoutOccurrenceOutboxMessageFactory.DeserializePointer(pointer.Payload!);
        var elapsed = Stopwatch.StartNew();
        var loaded = await repository.GetByPointerAsync(contract);
        var wrongTenant = await repository.GetByPointerAsync(contract with { TenantId = Guid.CreateVersion7() });
        elapsed.Stop();
        PersistenceQueryBaselineEvidence.Record(baseline
            .Snapshot("notification_occurrence_pointer_reads", loaded is null ? 0 : 1, elapsed.Elapsed));

        await Assert.That(loaded).IsNotNull();
        await Assert.That(loaded!.SafeAfterSnapshotJson).Contains("09:00:00Z");
        await Assert.That(wrongTenant).IsNull();
        await Assert.That(pointer.Payload!).DoesNotContain("title", StringComparison.OrdinalIgnoreCase);
        await Assert.That(pointer.Payload!).DoesNotContain("location", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task Create_WithEventFromAnotherTenant_FailsCompositeForeignKey()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario tenantA = await CreateScenarioAsync(context, "occurrence-fk-a");
        Scenario tenantB = await CreateScenarioAsync(context, "occurrence-fk-b");
        var occurrence = CreateOccurrence(tenantA.TenantId, tenantB.EventId);

        context.NotificationFanoutOccurrences.Add(occurrence);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task NotificationIntent_AllowsOneRecipientPerOccurrence()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "occurrence-dedup", includeRecipient: true);
        var occurrence = CreateOccurrence(scenario.TenantId, scenario.EventId);
        context.NotificationFanoutOccurrences.Add(occurrence);
        await context.SaveChangesAsync();

        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var repository = new NotificationIntentRepository(context, mutationLock);
        await mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            outerToken => unitOfWork.ExecuteInTransactionAsync(token =>
                repository.CreateGraphAsync(CreateIntent(scenario, occurrence.Id, "fanout:first"), token), outerToken));

        await Assert.ThrowsAsync<NotificationIntentDeduplicationConflictException>(() =>
            mutationLock.ExecuteOrderedGroupsAsync(
                [[GovernanceSettingKeys.Email.DeliveryEnabled]],
                outerToken => unitOfWork.ExecuteInTransactionAsync(token =>
                    repository.CreateGraphAsync(CreateIntent(scenario, occurrence.Id, "fanout:second"), token), outerToken)));
    }

    [Test]
    public async Task UnitOfWork_WhenMutationFails_RollsBackOccurrenceAndPointer()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "occurrence-rollback");
        var occurrence = CreateOccurrence(scenario.TenantId, scenario.EventId);
        OutboxMessage pointer = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence);
        var repository = new NotificationFanoutOccurrenceRepository(context);
        var outboxRepository = new OutboxRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);

        await Assert.ThrowsAsync<InjectedMutationFailureException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async _ =>
            {
                await repository.Create(occurrence);
                await outboxRepository.Create(pointer);
                throw new InjectedMutationFailureException();
            }));

        await using var verificationContext = fixture.CreateDbContext();
        bool occurrenceExists = await verificationContext.NotificationFanoutOccurrences
            .AnyAsync(row => row.Id == occurrence.Id);
        bool pointerExists = await verificationContext.OutboxMessages
            .AnyAsync(row => row.Id == pointer.Id);

        await Assert.That(occurrenceExists).IsFalse();
        await Assert.That(pointerExists).IsFalse();
    }

    [Test]
    public async Task CoordinationLock_WithoutCallerTransaction_FailsClosed()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "coordination-no-transaction");
        var repository = new NotificationFanoutOccurrenceRepository(context);
        NotificationFanoutOccurrenceCandidate candidate = CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            DateTime.UtcNow,
            sequence: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository
            .AcquireSourceThenEventCoordinationLocksAsync(
                candidate.TenantId,
                candidate.SourceType,
                candidate.SourceId,
                candidate.AggregateVersion,
                candidate.EventId));
    }

    [Test]
    public async Task SourceIdentityLookup_IsTenantWideAndNotLimitedToTheLockedEvent()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "coordination-source-scope");
        Guid otherEventId = await CreateEventAsync(context, scenario.TenantId, "Other source event");
        NotificationFanoutOccurrenceCandidate candidate = CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            DateTime.UtcNow,
            sequence: 1);
        NotificationFanoutOccurrence occurrence = CreatePersistedCandidate(candidate);
        var repository = new NotificationFanoutOccurrenceRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.Create(occurrence);
            await repository.AcquireSourceThenEventCoordinationLocksAsync(
                scenario.TenantId,
                candidate.SourceType,
                candidate.SourceId,
                candidate.AggregateVersion,
                otherEventId,
                token);
            NotificationFanoutOccurrence? replay = await repository.GetBySourceIdentityForCoordinationAsync(
                scenario.TenantId,
                candidate.SourceType,
                candidate.SourceId,
                candidate.AggregateVersion,
                token);
            await Assert.That(replay).IsNotNull();
            await Assert.That(replay!.EventId).IsEqualTo(scenario.EventId);
        });
    }

    [Test]
    public async Task SessionAuthority_RequiresExactTenantEventAndSessionBinding()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(context, "coordination-session-authority");
        Guid otherEventId = await CreateEventAsync(context, scenario.TenantId, "Session owner event");
        Guid sessionId = await CreateSessionAsync(context, scenario.TenantId, otherEventId, "Foreign session");
        NotificationFanoutOccurrenceCandidate candidate = CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            DateTime.UtcNow,
            sequence: 1);
        var repository = new NotificationFanoutOccurrenceRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.AcquireSourceThenEventCoordinationLocksAsync(
                candidate.TenantId,
                candidate.SourceType,
                candidate.SourceId,
                candidate.AggregateVersion,
                candidate.EventId,
                token);
            bool wrongEvent = await repository.SessionBelongsToEventForCoordinationAsync(
                scenario.TenantId,
                scenario.EventId,
                sessionId,
                token);
            bool ownerEvent = await repository.SessionBelongsToEventForCoordinationAsync(
                scenario.TenantId,
                otherEventId,
                sessionId,
                token);

            await Assert.That(wrongEvent).IsFalse();
            await Assert.That(ownerEvent).IsTrue();
        });
    }

    [Test]
    public async Task ConditionalSupersession_WhenPendingStateChanged_ReturnsFalse()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(seedContext, "coordination-conditional");
        DateTime at = DateTime.UtcNow;
        NotificationFanoutOccurrence old = CreatePersistedCandidate(CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at,
            sequence: 1));
        NotificationFanoutOccurrence firstWinner = CreatePersistedCandidate(CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at.AddMinutes(1),
            sequence: 2));
        NotificationFanoutOccurrence secondWinner = CreatePersistedCandidate(CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at.AddMinutes(2),
            sequence: 3));
        seedContext.NotificationFanoutOccurrences.AddRange(old, firstWinner, secondWinner);
        await seedContext.SaveChangesAsync();

        await using var staleContext = fixture.CreateDbContext();
        var staleRepository = new NotificationFanoutOccurrenceRepository(staleContext);
        NotificationFanoutOccurrence stale = (await staleRepository.GetByPointerAsync(
            new NotificationFanoutOccurrenceRequested(
                scenario.TenantId,
                old.Id,
                NotificationFanoutOccurrenceRequested.CurrentVersion)))!;

        await using (var competingContext = fixture.CreateDbContext())
        {
            var competingRepository = new NotificationFanoutOccurrenceRepository(competingContext);
            var competingUnitOfWork = new EfCoreUnitOfWork(competingContext);
            await competingUnitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await competingRepository.AcquireSourceThenEventCoordinationLocksAsync(
                    scenario.TenantId,
                    old.SourceType,
                    old.SourceId,
                    old.AggregateVersion,
                    scenario.EventId,
                    token);
                NotificationFanoutOccurrence competing = (await competingRepository.GetByPointerAsync(
                    new NotificationFanoutOccurrenceRequested(
                        scenario.TenantId,
                        old.Id,
                        NotificationFanoutOccurrenceRequested.CurrentVersion),
                    cancellationToken: token))!;
                competing.Supersede(firstWinner.Id, "superseded_by_newer_update", at.AddMinutes(1));
                await Assert.That(await competingRepository.TryPersistSupersessionAsync(competing, token)).IsTrue();
            });
        }

        stale.Supersede(secondWinner.Id, "superseded_by_newer_update", at.AddMinutes(2));
        var staleUnitOfWork = new EfCoreUnitOfWork(staleContext);
        bool changed = await staleUnitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await staleRepository.AcquireSourceThenEventCoordinationLocksAsync(
                scenario.TenantId,
                stale.SourceType,
                stale.SourceId,
                stale.AggregateVersion,
                scenario.EventId,
                token);
            return await staleRepository.TryPersistSupersessionAsync(stale, token);
        });

        await Assert.That(changed).IsFalse();
    }

    [Test]
    public async Task HeavyCoordination_AfterRunHandoff_SettlesNonterminalRunsAndPreservesTerminalEvidence()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(seedContext, "coordination-heavy-run-settlement");
        Guid sessionA = await CreateSessionAsync(seedContext, scenario.TenantId, scenario.EventId, "Session A");
        Guid sessionB = await CreateSessionAsync(seedContext, scenario.TenantId, scenario.EventId, "Session B");
        Guid sessionC = await CreateSessionAsync(seedContext, scenario.TenantId, scenario.EventId, "Session C");
        DateTime at = DateTime.UtcNow.AddMinutes(-10);
        NotificationFanoutOccurrence eventUpdate = CreatePersistedCandidate(CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at,
            sequence: 1));
        NotificationFanoutOccurrence sessionUpdateA = CreatePersistedCandidate(WithSessionScope(CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at.AddMinutes(1),
            sequence: 2), sessionA, "Session A"));
        NotificationFanoutOccurrence sessionUpdateB = CreatePersistedCandidate(WithSessionScope(CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at.AddMinutes(2),
            sequence: 3), sessionB, "Session B"));
        NotificationFanoutOccurrence sessionUpdateC = CreatePersistedCandidate(WithSessionScope(CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at.AddMinutes(3),
            sequence: 4), sessionC, "Session C"));
        seedContext.NotificationFanoutOccurrences.AddRange(
            eventUpdate,
            sessionUpdateA,
            sessionUpdateB,
            sessionUpdateC);
        await seedContext.SaveChangesAsync();

        var runIds = new Dictionary<Guid, Guid>();
        foreach (Guid occurrenceId in new[]
        {
            eventUpdate.Id,
            sessionUpdateA.Id,
            sessionUpdateB.Id,
            sessionUpdateC.Id
        })
        {
            await using var handoffContext = fixture.CreateDbContext();
            var runRepository = new NotificationFanoutRunRepository(handoffContext);
            NotificationFanoutRun run = await runRepository.EnsurePendingOccurrenceRunAsync(
                    scenario.TenantId,
                    occurrenceId,
                    Guid.CreateVersion7(),
                    CancellationToken.None)
                ?? throw new InvalidOperationException("Pending occurrence handoff did not create its run.");
            runIds.Add(occurrenceId, run.Id);
        }

        Guid processingLeaseToken = Guid.CreateVersion7();
        DateTime cursorAt = DateTime.UtcNow.AddHours(-1);
        NotificationFanoutClaim staleClaim;
        RunTerminalEvidence completedEvidence;
        RunTerminalEvidence failedEvidence;
        await using (var preparationContext = fixture.CreateDbContext())
        {
            NotificationFanoutRun pendingRun = await preparationContext.NotificationFanoutRuns
                .SingleAsync(run => run.Id == runIds[eventUpdate.Id]);
            pendingRun.CursorFirstEligibleRegistrationCreatedAt = cursorAt;
            pendingRun.CursorUserId = Guid.CreateVersion7();
            pendingRun.ProcessedCount = 11;
            pendingRun.CreatedNotificationCount = 7;

            NotificationFanoutRun processingRun = await preparationContext.NotificationFanoutRuns
                .SingleAsync(run => run.Id == runIds[sessionUpdateA.Id]);
            processingRun.Status = "processing";
            processingRun.CursorFirstEligibleRegistrationCreatedAt = cursorAt.AddMinutes(1);
            processingRun.CursorUserId = Guid.CreateVersion7();
            processingRun.ProcessingLeaseOwner = "stale-worker";
            processingRun.ProcessingLeaseToken = processingLeaseToken;
            processingRun.ProcessingLeaseExpiresAt = DateTime.UtcNow.AddHours(1);
            processingRun.ProcessingGeneration = 3;
            processingRun.ProcessingFence = 5;
            processingRun.ProcessedCount = 13;
            processingRun.CreatedNotificationCount = 8;
            processingRun.StartedAt = DateTime.UtcNow.AddMinutes(-2);

            NotificationFanoutRun completedRun = await preparationContext.NotificationFanoutRuns
                .SingleAsync(run => run.Id == runIds[sessionUpdateB.Id]);
            completedRun.Status = "completed";
            completedRun.ProcessedCount = 17;
            completedRun.CreatedNotificationCount = 9;
            completedRun.CompletedAt = DateTime.UtcNow.AddMinutes(-1);
            completedRun.UpdatedAt = completedRun.CompletedAt;

            NotificationFanoutRun failedRun = await preparationContext.NotificationFanoutRuns
                .SingleAsync(run => run.Id == runIds[sessionUpdateC.Id]);
            failedRun.Status = "failed";
            failedRun.ProcessedCount = 19;
            failedRun.CreatedNotificationCount = 10;
            failedRun.FailedAt = DateTime.UtcNow.AddMinutes(-1);
            failedRun.LastError = "retained bounded failure evidence";
            failedRun.UpdatedAt = failedRun.FailedAt;
            await preparationContext.SaveChangesAsync();

            staleClaim = new NotificationFanoutClaim(
                processingRun.Id,
                processingRun.TenantId,
                processingRun.FanoutOccurrenceId!.Value,
                processingLeaseToken,
                processingRun.ProcessingFence,
                processingRun.ProcessingGeneration,
                new NotificationFanoutAudienceCursor(
                    processingRun.CursorFirstEligibleRegistrationCreatedAt!.Value,
                    processingRun.CursorUserId!.Value));
            completedEvidence = CaptureRunTerminalEvidence(completedRun);
            failedEvidence = CaptureRunTerminalEvidence(failedRun);
        }

        await using (var heavyContext = fixture.CreateDbContext())
        {
            var unitOfWork = new EfCoreUnitOfWork(heavyContext);
            var mutationLock = new RelationalSettingMutationLock(heavyContext, unitOfWork);
            var coordinator = new NotificationFanoutOccurrenceCoordinator(
                new NotificationFanoutOccurrenceRepository(heavyContext),
                new NotificationFanoutEmailSuppressionRepository(heavyContext),
                new OutboxRepository(heavyContext),
                new NotificationFanoutRecipientTemplateFactory(),
                new EmailDispatchOutboxRepository(heavyContext),
                mutationLock);
            DateTime heavyAt = DateTime.UtcNow;
            await mutationLock.ExecuteOrderedGroupsAsync(
                [[GovernanceSettingKeys.Email.DeliveryEnabled]],
                outerToken => unitOfWork.ExecuteInTransactionAsync(token => coordinator.CoordinateInCurrentTransactionAsync(
                new NotificationFanoutOccurrenceCandidate(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    scenario.TenantId,
                    scenario.EventId,
                    SessionId: null,
                    heavyAt,
                    AudienceCutoffAt: heavyAt,
                    Guid.CreateVersion7(),
                    ChangeSetJson: "{}",
                    SafeBeforeSnapshotJson: "{}",
                    SafeAfterSnapshotJson: "{}",
                    NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey,
                    NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
                    (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired,
                    NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
                    RequestedNotBefore: heavyAt,
                    SourceType: "event_moderation_record",
                    SourceId: Guid.CreateVersion7()),
                token), outerToken));
        }

        await using var verificationContext = fixture.CreateDbContext();
        NotificationFanoutRun[] settledRuns = await verificationContext.NotificationFanoutRuns
            .AsNoTracking()
            .Where(run => run.TenantId == scenario.TenantId
                && runIds.Values.Contains(run.Id))
            .ToArrayAsync();
        NotificationFanoutRun settledPending = settledRuns.Single(run => run.Id == runIds[eventUpdate.Id]);
        NotificationFanoutRun settledProcessing = settledRuns.Single(run => run.Id == runIds[sessionUpdateA.Id]);
        NotificationFanoutRun unchangedCompleted = settledRuns.Single(run => run.Id == runIds[sessionUpdateB.Id]);
        NotificationFanoutRun unchangedFailed = settledRuns.Single(run => run.Id == runIds[sessionUpdateC.Id]);

        await Assert.That(settledRuns.Count(run => run.Status is "pending" or "processing")).IsEqualTo(0);
        await Assert.That(settledPending.Status).IsEqualTo("completed");
        await Assert.That(settledPending.ProcessedCount).IsEqualTo(11);
        await Assert.That(settledPending.CreatedNotificationCount).IsEqualTo(7);
        await Assert.That(settledProcessing.Status).IsEqualTo("completed");
        await Assert.That(settledProcessing.ProcessedCount).IsEqualTo(13);
        await Assert.That(settledProcessing.CreatedNotificationCount).IsEqualTo(8);
        await Assert.That(settledProcessing.ProcessingLeaseOwner).IsNull();
        await Assert.That(settledProcessing.ProcessingLeaseToken).IsNull();
        await Assert.That(settledProcessing.ProcessingLeaseExpiresAt).IsNull();
        await Assert.That(CaptureRunTerminalEvidence(unchangedCompleted)).IsEqualTo(completedEvidence);
        await Assert.That(CaptureRunTerminalEvidence(unchangedFailed)).IsEqualTo(failedEvidence);

        var staleWorkerRepository = new NotificationFanoutRunRepository(verificationContext);
        DateTime observedAt = DateTime.UtcNow;
        NotificationFanoutAudienceCursor nextCursor = new(cursorAt.AddHours(1), Guid.CreateVersion7());
        await Assert.That(await staleWorkerRepository.TryRenewClaimAsync(
            staleClaim,
            observedAt,
            observedAt.AddMinutes(1),
            CancellationToken.None)).IsFalse();
        await Assert.That(await staleWorkerRepository.TryCheckpointAsync(
            staleClaim,
            staleClaim.Cursor,
            nextCursor,
            processedDelta: 1,
            createdDelta: 1,
            observedAt,
            CancellationToken.None)).IsFalse();
        await Assert.That(await staleWorkerRepository.TryCompleteAsync(
            staleClaim,
            observedAt,
            CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task ConcurrentCoordinators_ProduceOnePendingWinnerWithoutDuplicateSourceRows()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(seedContext, "coordination-concurrent");
        DateTime at = DateTime.UtcNow;
        NotificationFanoutOccurrenceCandidate first = CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at,
            sequence: 1);
        NotificationFanoutOccurrenceCandidate second = CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at.AddMinutes(1),
            sequence: 2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<NotificationFanoutOccurrenceCoordinationResult> CoordinateAsync(
            NotificationFanoutOccurrenceCandidate candidate)
        {
            await using var context = fixture.CreateDbContext();
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var coordinator = new NotificationFanoutOccurrenceCoordinator(
                new NotificationFanoutOccurrenceRepository(context),
                new NotificationFanoutEmailSuppressionRepository(context),
                new OutboxRepository(context),
                new NotificationFanoutRecipientTemplateFactory(),
                new EmailDispatchOutboxRepository(context),
                mutationLock);
            await release.Task;
            return await mutationLock.ExecuteOrderedGroupsAsync(
                [[GovernanceSettingKeys.Email.DeliveryEnabled]],
                outerToken => unitOfWork.ExecuteInTransactionAsync(token =>
                    coordinator.CoordinateInCurrentTransactionAsync(candidate, token), outerToken));
        }

        Task<NotificationFanoutOccurrenceCoordinationResult> firstTask = CoordinateAsync(first);
        Task<NotificationFanoutOccurrenceCoordinationResult> secondTask = CoordinateAsync(second);
        release.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        await using var verificationContext = fixture.CreateDbContext();
        List<NotificationFanoutOccurrence> occurrences = await verificationContext.NotificationFanoutOccurrences
            .Where(value => value.TenantId == scenario.TenantId && value.EventId == scenario.EventId)
            .OrderBy(value => value.OccurredAt)
            .ToListAsync();
        NotificationFanoutOccurrence active = occurrences.Single(value => value.State == NotificationFanoutOccurrenceState.Pending);
        NotificationFanoutOccurrence superseded = occurrences.Single(value => value.State == NotificationFanoutOccurrenceState.Superseded);

        await Assert.That(occurrences).Count().IsEqualTo(2);
        await Assert.That(active.Id).IsEqualTo(second.OccurrenceId);
        await Assert.That(superseded.SupersededByOccurrenceId).IsEqualTo(active.Id);
        await Assert.That(occurrences.Select(value => new
        {
            value.TenantId,
            value.SourceType,
            value.SourceId,
            value.AggregateVersion
        }).Distinct()).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ConcurrentDifferentEventCoordinators_WithSameSourceTuple_PersistOneAndFailClosed()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(seedContext, "coordination-source-race");
        Guid otherEventId = await CreateEventAsync(seedContext, scenario.TenantId, "Competing source event");
        DateTime at = DateTime.UtcNow;
        NotificationFanoutOccurrenceCandidate first = CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            at,
            sequence: 1);
        NotificationFanoutOccurrenceCandidate second = first with
        {
            OccurrenceId = Guid.CreateVersion7(),
            PointerOutboxMessageId = Guid.CreateVersion7(),
            EventId = otherEventId
        };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> TryCoordinateAsync(NotificationFanoutOccurrenceCandidate candidate)
        {
            await using var context = fixture.CreateDbContext();
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var coordinator = new NotificationFanoutOccurrenceCoordinator(
                new NotificationFanoutOccurrenceRepository(context),
                new NotificationFanoutEmailSuppressionRepository(context),
                new OutboxRepository(context),
                new NotificationFanoutRecipientTemplateFactory(),
                new EmailDispatchOutboxRepository(context),
                mutationLock);
            await release.Task;
            try
            {
                await mutationLock.ExecuteOrderedGroupsAsync(
                    [[GovernanceSettingKeys.Email.DeliveryEnabled]],
                    outerToken => unitOfWork.ExecuteInTransactionAsync(token =>
                        coordinator.CoordinateInCurrentTransactionAsync(candidate, token), outerToken));
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        Task<Exception?> firstTask = TryCoordinateAsync(first);
        Task<Exception?> secondTask = TryCoordinateAsync(second);
        release.SetResult();
        Exception?[] outcomes = await Task.WhenAll(firstTask, secondTask);

        await Assert.That(outcomes.Count(outcome => outcome is null)).IsEqualTo(1);
        await Assert.That(outcomes.Count(outcome => outcome is InvalidOperationException)).IsEqualTo(1);
        await using var verificationContext = fixture.CreateDbContext();
        List<NotificationFanoutOccurrence> persisted = await verificationContext.NotificationFanoutOccurrences
            .Where(value => value.TenantId == first.TenantId
                && value.SourceType == first.SourceType
                && value.SourceId == first.SourceId
                && value.AggregateVersion == first.AggregateVersion)
            .ToListAsync();
        int pointerCount = await verificationContext.OutboxMessages.CountAsync(value =>
            value.EventType == NotificationFanoutOccurrenceOutboxMessageFactory.EventType
            && (value.AggregateId == first.OccurrenceId || value.AggregateId == second.OccurrenceId));

        await Assert.That(persisted).Count().IsEqualTo(1);
        await Assert.That(pointerCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExactReplay_WithSubMicrosecondInput_UsesPersistedTimestampPrecision()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();
        Scenario scenario = await CreateScenarioAsync(seedContext, "coordination-timestamp-replay");
        DateTime occurredAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc).AddTicks(7);
        NotificationFanoutOccurrenceCandidate candidate = CreateCoordinationCandidate(
            scenario.TenantId,
            scenario.EventId,
            occurredAt,
            sequence: 1);

        await using (var firstContext = fixture.CreateDbContext())
        {
            var firstUnitOfWork = new EfCoreUnitOfWork(firstContext);
            var firstMutationLock = new RelationalSettingMutationLock(firstContext, firstUnitOfWork);
            var firstCoordinator = new NotificationFanoutOccurrenceCoordinator(
                new NotificationFanoutOccurrenceRepository(firstContext),
                new NotificationFanoutEmailSuppressionRepository(firstContext),
                new OutboxRepository(firstContext),
                new NotificationFanoutRecipientTemplateFactory(),
                new EmailDispatchOutboxRepository(firstContext),
                firstMutationLock);
            NotificationFanoutOccurrenceCoordinationResult first = await firstMutationLock.ExecuteOrderedGroupsAsync(
                [[GovernanceSettingKeys.Email.DeliveryEnabled]],
                outerToken => firstUnitOfWork.ExecuteInTransactionAsync(
                    token => firstCoordinator.CoordinateInCurrentTransactionAsync(candidate, token), outerToken));
            await Assert.That(first.Outcome).IsEqualTo(NotificationFanoutOccurrenceCoordinationOutcome.NewlyActive);
            await Assert.That(first.Occurrence.OccurredAt.Ticks % TimeSpan.TicksPerMicrosecond).IsEqualTo(0);
        }

        await using var replayContext = fixture.CreateDbContext();
        var replayUnitOfWork = new EfCoreUnitOfWork(replayContext);
        var replayMutationLock = new RelationalSettingMutationLock(replayContext, replayUnitOfWork);
        var replayCoordinator = new NotificationFanoutOccurrenceCoordinator(
            new NotificationFanoutOccurrenceRepository(replayContext),
            new NotificationFanoutEmailSuppressionRepository(replayContext),
            new OutboxRepository(replayContext),
            new NotificationFanoutRecipientTemplateFactory(),
            new EmailDispatchOutboxRepository(replayContext),
            replayMutationLock);
        NotificationFanoutOccurrenceCoordinationResult replay = await replayMutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            outerToken => replayUnitOfWork.ExecuteInTransactionAsync(
                token => replayCoordinator.CoordinateInCurrentTransactionAsync(candidate, token), outerToken));
        int persistedCount = await replayContext.NotificationFanoutOccurrences.CountAsync(value =>
            value.TenantId == candidate.TenantId
            && value.SourceType == candidate.SourceType
            && value.SourceId == candidate.SourceId
            && value.AggregateVersion == candidate.AggregateVersion);

        await Assert.That(replay.Outcome).IsEqualTo(NotificationFanoutOccurrenceCoordinationOutcome.SourceReplay);
        await Assert.That(replay.Occurrence.Id).IsEqualTo(candidate.OccurrenceId);
        await Assert.That(persistedCount).IsEqualTo(1);
    }

    private static NotificationFanoutOccurrenceCandidate CreateCoordinationCandidate(
        Guid tenantId,
        Guid eventId,
        DateTime occurredAt,
        int sequence)
    {
        string before = NotificationFanoutTemplateJson.Serialize(new NotificationFanoutSnapshotV1(
            "Concurrent event",
            null,
            new DateTimeOffset(occurredAt.AddHours(sequence)),
            new DateTimeOffset(occurredAt.AddHours(sequence + 1)),
            "UTC",
            null));
        string after = NotificationFanoutTemplateJson.Serialize(new NotificationFanoutSnapshotV1(
            "Concurrent event",
            null,
            new DateTimeOffset(occurredAt.AddHours(sequence + 1)),
            new DateTimeOffset(occurredAt.AddHours(sequence + 2)),
            "UTC",
            null));
        return new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            tenantId,
            eventId,
            null,
            occurredAt,
            occurredAt,
            Guid.CreateVersion7(),
            NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1([
                NotificationFanoutChangeField.StartTime])),
            before,
            after,
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
            (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
            occurredAt,
            "event-mutation",
            Guid.CreateVersion7());
    }

    private static NotificationFanoutOccurrence CreatePersistedCandidate(
        NotificationFanoutOccurrenceCandidate candidate)
    {
        DateTime notBefore = candidate.OccurredAt.AddMinutes(5);
        return NotificationFanoutOccurrence.Create(
            candidate.OccurrenceId,
            candidate.TenantId,
            candidate.EventId,
            candidate.SessionId,
            candidate.OccurredAt,
            candidate.AudienceCutoffAt,
            candidate.AggregateVersion,
            candidate.ChangeSetJson,
            candidate.SafeBeforeSnapshotJson,
            candidate.SafeAfterSnapshotJson,
            candidate.TemplateKey,
            candidate.TemplateVersion,
            candidate.DeliveryPolicyId,
            candidate.PolicyVersion,
            NotificationFanoutOccurrenceCoordinationPolicy.ImportantUpdatePriority,
            notBefore,
            candidate.SourceType,
            candidate.SourceId,
            candidate.SessionId.HasValue
                ? $"event:{candidate.EventId:N}:session:{candidate.SessionId.Value:N}"
                : $"event:{candidate.EventId:N}",
            notBefore,
            emailDeliveryPolicyRevision: 0);
    }

    private static NotificationFanoutOccurrenceCandidate WithSessionScope(
        NotificationFanoutOccurrenceCandidate candidate,
        Guid sessionId,
        string sessionTitle)
    {
        string before = NotificationFanoutTemplateJson.Serialize(new NotificationFanoutSnapshotV1(
            "Concurrent event",
            sessionTitle,
            new DateTimeOffset(candidate.OccurredAt),
            new DateTimeOffset(candidate.OccurredAt.AddHours(1)),
            "UTC",
            null));
        string after = NotificationFanoutTemplateJson.Serialize(new NotificationFanoutSnapshotV1(
            "Concurrent event",
            sessionTitle,
            new DateTimeOffset(candidate.OccurredAt.AddHours(1)),
            new DateTimeOffset(candidate.OccurredAt.AddHours(2)),
            "UTC",
            null));

        return candidate with
        {
            SessionId = sessionId,
            SafeBeforeSnapshotJson = before,
            SafeAfterSnapshotJson = after,
            TemplateKey = NotificationFanoutRecipientTemplateFactory.SessionUpdatedTemplateKey,
            SourceId = sessionId
        };
    }

    private static RunTerminalEvidence CaptureRunTerminalEvidence(NotificationFanoutRun run) => new(
        run.Status,
        AtPostgresPrecision(run.CursorFirstEligibleRegistrationCreatedAt),
        run.CursorUserId,
        run.ProcessingLeaseOwner,
        run.ProcessingLeaseToken,
        AtPostgresPrecision(run.ProcessingLeaseExpiresAt),
        run.ProcessingGeneration,
        run.ProcessingFence,
        run.ProcessedCount,
        run.CreatedNotificationCount,
        AtPostgresPrecision(run.StartedAt),
        AtPostgresPrecision(run.CompletedAt),
        AtPostgresPrecision(run.FailedAt),
        run.LastError,
        AtPostgresPrecision(run.UpdatedAt),
        run.ConcurrencyStamp);

    private static async Task<Guid> CreateEventAsync(
        ExploreDbContext context,
        Guid tenantId,
        string title)
    {
        Guid actorId = await context.Actors
            .Select(value => value.Id)
            .SingleAsync();
        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = await context.Tenants.SingleAsync(value => value.Id == tenantId),
            Title = title,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actorId,
            Actor = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = await context.VisibilityTypes.SingleAsync(
                value => value.Id == (int)VisibilityTypeEnum.Public),
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();
        return @event.Id;
    }

    private static async Task<Guid> CreateSessionAsync(
        ExploreDbContext context,
        Guid tenantId,
        Guid eventId,
        string title)
    {
        var session = new EventSession(EventSessionStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            EventId = eventId,
            Event = null!,
            Title = title,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow
        };
        context.EventSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private static NotificationFanoutOccurrence CreateOccurrence(
        Guid tenantId,
        Guid eventId,
        Guid? sourceId = null)
    {
        DateTime occurredAt = DateTime.UtcNow;
        return NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(), tenantId, eventId, null,
            occurredAt, occurredAt, Guid.CreateVersion7(),
            "{\"fields\":[\"startTime\"]}",
            "{\"startTime\":\"2026-07-18T08:00:00Z\"}",
            "{\"startTime\":\"2026-07-18T09:00:00Z\"}",
            "event.session.updated", 1,
            (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional, 1,
            30, occurredAt.AddMinutes(5), "event", sourceId ?? eventId,
            $"event:{eventId:N}:schedule", occurredAt.AddMinutes(5),
            emailDeliveryPolicyRevision: 0);
    }

    private static async Task<SuppressionGraph> CreateSuppressionGraphAsync(
        ExploreDbContext context,
        Scenario scenario,
        NotificationFanoutOccurrence occurrence,
        EmailDispatchStatus status,
        bool providerFenced,
        NotificationDeliveryStatusEnum inAppStatus = NotificationDeliveryStatusEnum.Delivered)
    {
        DateTime now = AtPostgresPrecision(DateTime.UtcNow);
        Guid recipientUserId = scenario.RecipientUserId!.Value;
        TenantUser tenantUser = await context.TenantUsers
            .SingleAsync(value => value.TenantId == scenario.TenantId && value.UserId == recipientUserId);
        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            CategoryId = (int)NotificationCategoryEnum.EventLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = occurrence.TemplateKey,
            DeduplicationKey = $"suppression:{occurrence.Id:N}:{Guid.CreateVersion7():N}",
            RecipientUserId = recipientUserId,
            RecipientTenantUser = tenantUser,
            FanoutOccurrenceId = occurrence.Id,
            EventId = scenario.EventId,
            CreatedAt = now
        };
        int attemptCount = status == EmailDispatchStatus.Pending ? 0 : 3;
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.EventUpdated,
            SourceType = "notification_intent",
            SourceId = intent.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            EventId = scenario.EventId,
            RecipientUserId = recipientUserId,
            RecipientTenantUser = tenantUser,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = "fanout-recipient@example.test",
            Subject = "Event updated",
            PlainTextBody = "Event information changed.",
            Status = status,
            AttemptCount = attemptCount,
            MaxAttempts = 5,
            NextAttemptAt = status == EmailDispatchStatus.RetryScheduled ? now.AddMinutes(5) : null,
            ProcessingStartedAt = status == EmailDispatchStatus.Processing ? now : null,
            ProcessingLeaseToken = status == EmailDispatchStatus.Processing ? Guid.CreateVersion7() : null,
            SentAt = status == EmailDispatchStatus.Sent ? now : null,
            DeadLetteredAt = status == EmailDispatchStatus.DeadLettered ? now : null,
            ParkedAt = status == EmailDispatchStatus.Parked ? now : null,
            UnknownAt = status == EmailDispatchStatus.Unknown ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        int deliveryStatusId = status switch
        {
            EmailDispatchStatus.Sent => (int)NotificationDeliveryStatusEnum.Delivered,
            EmailDispatchStatus.Unknown => (int)NotificationDeliveryStatusEnum.Unknown,
            EmailDispatchStatus.DeadLettered => (int)NotificationDeliveryStatusEnum.DeadLettered,
            EmailDispatchStatus.Parked => (int)NotificationDeliveryStatusEnum.Parked,
            EmailDispatchStatus.Skipped => (int)NotificationDeliveryStatusEnum.Skipped,
            _ => (int)NotificationDeliveryStatusEnum.Queued
        };
        var delivery = new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            IsRequired = false,
            PolicyVersion = 1,
            PreferenceCategoryCode = "event-updates",
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            DisclosureLevel = "standard",
            TemplateKey = occurrence.TemplateKey,
            TemplateVersion = 1,
            LinkAllowed = false,
            EmailDispatchOutboxId = dispatch.Id,
            EmailDispatchOutbox = dispatch,
            StatusId = deliveryStatusId,
            ProviderStatus = status.ToString().ToLowerInvariant(),
            QueuedAt = now,
            CompletedAt = status is EmailDispatchStatus.Sent
                or EmailDispatchStatus.Unknown
                or EmailDispatchStatus.DeadLettered
                or EmailDispatchStatus.Parked
                or EmailDispatchStatus.Skipped
                    ? now
                    : null,
            CreatedAt = now
        };
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            Tenant = null!,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            UserId = recipientUserId,
            User = null!,
            NotificationTypeId = (int)NotificationTypeEnum.EventUpdated,
            NotificationType = null!,
            Title = "Stale event details",
            Body = "This stale notification must become unavailable.",
            DeduplicationKey = $"{intent.DeduplicationKey}:in-app",
            NotificationScopeId = (int)ActorTypeEnum.User,
            NotificationScope = null!,
            NotificationReasonId = (int)NotificationReasonEnum.System,
            CreatedAt = now
        };
        var inAppDelivery = new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.InApp,
            DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            IsRequired = false,
            PolicyVersion = 1,
            DisclosureLevel = "standard",
            TemplateKey = occurrence.TemplateKey,
            TemplateVersion = 1,
            LinkAllowed = false,
            NotificationId = notification.Id,
            Notification = notification,
            StatusId = (int)inAppStatus,
            CompletedAt = inAppStatus == NotificationDeliveryStatusEnum.Delivered ? now : null,
            CreatedAt = now
        };
        intent.Deliveries.Add(delivery);
        intent.Deliveries.Add(inAppDelivery);
        var attempts = new List<EmailDispatchAttempt>();
        var receipts = new List<EmailDispatchReceipt>();
        if (status != EmailDispatchStatus.Pending)
        {
            int attemptNumber = status == EmailDispatchStatus.Processing && !providerFenced
                ? attemptCount - 1
                : attemptCount;
            string failureCategory = providerFenced ? "provider_handoff_started" : "existing_evidence";
            var attempt = new EmailDispatchAttempt
            {
                Id = Guid.CreateVersion7(),
                TenantId = scenario.TenantId,
                EmailDispatchOutboxId = dispatch.Id,
                EmailDispatchOutbox = dispatch,
                AttemptNumber = attemptNumber,
                Outcome = providerFenced || status == EmailDispatchStatus.Unknown
                    ? EmailDispatchAttemptOutcome.Unknown
                    : status == EmailDispatchStatus.Sent
                        ? EmailDispatchAttemptOutcome.Succeeded
                        : status == EmailDispatchStatus.Skipped
                            ? EmailDispatchAttemptOutcome.Skipped
                            : EmailDispatchAttemptOutcome.Failed,
                StartedAt = now,
                CompletedAt = providerFenced ? null : now,
                FailureCategory = failureCategory,
                SanitizedErrorMessage = "Existing non-PII evidence.",
                CreatedAt = now
            };
            var receipt = new EmailDispatchReceipt
            {
                Id = Guid.CreateVersion7(),
                TenantId = scenario.TenantId,
                PublishEventId = dispatch.PublishEventId,
                EmailDispatchOutboxId = dispatch.Id,
                EmailDispatchOutbox = dispatch,
                Status = providerFenced
                    ? EmailDispatchReceiptStatus.Processing
                    : status == EmailDispatchStatus.Sent
                        ? EmailDispatchReceiptStatus.Completed
                        : status == EmailDispatchStatus.Unknown
                            ? EmailDispatchReceiptStatus.Unknown
                            : status == EmailDispatchStatus.Skipped
                                ? EmailDispatchReceiptStatus.Skipped
                                : EmailDispatchReceiptStatus.Failed,
                ConsumerId = "test-worker",
                FirstSeenAt = now,
                ProcessingStartedAt = providerFenced ? now : null,
                CompletedAt = status == EmailDispatchStatus.Sent ? now : null,
                FailedAt = providerFenced || status == EmailDispatchStatus.Sent ? null : now,
                FailureCode = failureCategory,
                CreatedAt = now
            };
            attempts.Add(attempt);
            receipts.Add(receipt);
        }

        context.NotificationIntents.Add(intent);
        context.EmailDispatchOutbox.Add(dispatch);
        context.NotificationDeliveries.Add(delivery);
        context.Notifications.Add(notification);
        context.NotificationDeliveries.Add(inAppDelivery);
        context.EmailDispatchAttempts.AddRange(attempts);
        context.EmailDispatchReceipts.AddRange(receipts);
        await context.SaveChangesAsync();
        return new SuppressionGraph(
            dispatch.Id,
            dispatch.PublishEventId,
            intent.Id,
            delivery.Id,
            notification.Id,
            inAppDelivery.Id,
            inAppDelivery.CompletedAt,
            attemptCount,
            deliveryStatusId,
            attempts.Select(value => (value.AttemptNumber, value.Outcome, value.FailureCategory)).ToArray(),
            receipts.Select(value => (value.Status, value.FailureCode)).ToArray());
    }

    private static EmailDispatchEligibilityEvaluator CreateEligibilityEvaluator(ExploreDbContext context) =>
        new(
            context,
            new NotificationDeliveryPolicyResolver(),
            new NotificationPreferenceResolver(context),
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

    private static DateTime AtPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);

    private static DateTime? AtPostgresPrecision(DateTime? value) =>
        value.HasValue ? AtPostgresPrecision(value.Value) : null;

    private static NotificationIntent CreateIntent(Scenario scenario, Guid occurrenceId, string deduplicationKey)
    {
        return new NotificationIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            CategoryId = (int)NotificationCategoryEnum.EventLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.Pending,
            TemplateKey = "event.session.updated",
            DeduplicationKey = deduplicationKey,
            RecipientUserId = scenario.RecipientUserId!.Value,
            FanoutOccurrenceId = occurrenceId,
            EventId = scenario.EventId,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static async Task<Scenario> CreateScenarioAsync(
        ExploreDbContext context,
        string slugPrefix,
        bool includeRecipient = false)
    {
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Fanout {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var servicePrincipal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = $"fanout-source-{Guid.CreateVersion7():N}",
            DisplayName = "Fanout source",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Bot,
            ActorType = null!,
            ServicePrincipalId = servicePrincipal.Id,
            ServicePrincipal = servicePrincipal,
            Pii = new ActorPii { DisplayName = "Fanout source" },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Fanout event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        context.Events.Add(@event);

        Guid? recipientUserId = null;
        if (includeRecipient)
        {
            var user = new User
            {
                Id = Guid.CreateVersion7(),
                Pii = new UserPii
                {
                    Email = $"{slugPrefix}@example.test",
                    FirstName = "Fanout",
                    LastName = "Recipient",
                },
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
            };
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = null!,
                UserId = user.Id,
                User = user,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            recipientUserId = user.Id;
        }

        await context.SaveChangesAsync();
        return new Scenario(tenant.Id, @event.Id, recipientUserId);
    }

    private sealed record Scenario(Guid TenantId, Guid EventId, Guid? RecipientUserId);
    private sealed record RunTerminalEvidence(
        string Status,
        DateTime? CursorFirstEligibleRegistrationCreatedAt,
        Guid? CursorUserId,
        string? ProcessingLeaseOwner,
        Guid? ProcessingLeaseToken,
        DateTime? ProcessingLeaseExpiresAt,
        int ProcessingGeneration,
        long ProcessingFence,
        int ProcessedCount,
        int CreatedNotificationCount,
        DateTime? StartedAt,
        DateTime? CompletedAt,
        DateTime? FailedAt,
        string? LastError,
        DateTime? UpdatedAt,
        Guid ConcurrencyStamp);
    private sealed record SuppressionGraph(
        Guid DispatchId,
        Guid PublishEventId,
        Guid NotificationIntentId,
        Guid DeliveryId,
        Guid NotificationId,
        Guid InAppDeliveryId,
        DateTime? InAppDeliveryCompletedAt,
        int AttemptCount,
        int DeliveryStatusId,
        IReadOnlyList<(int AttemptNumber, EmailDispatchAttemptOutcome Outcome, string? FailureCategory)> Attempts,
        IReadOnlyList<(EmailDispatchReceiptStatus Status, string? FailureCode)> Receipts);
    private sealed class InjectedMutationFailureException : Exception;
}
