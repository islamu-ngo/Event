using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
[Category("EventLocationPrivacy")]
public sealed class LocationPrivacyCorrectionOutboxPostgreSqlTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RetryAfterPartialCorrection_PersistsPendingProcessingAndCompletedStates()
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(fixture, instance: true);
        CorrectionIdentity identity = new(rows.A.TenantId, rows.A.EventId, rows.A.Id);
        await ProduceAsync(rows);
        await using var context = fixture.CreateDbContext();
        var repository = new OutboxRepository(context);
        OutboxMessage message = await context.OutboxMessages.AsNoTracking().SingleAsync(row =>
            row.AggregateId == identity.EventLocationId
            && row.EventType == LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType);

        await Assert.That((await ReadAsync(message.Id)).Status).IsEqualTo(OutboxMessageStatus.Pending);

        DateTime claimedAt = DateTime.UtcNow;
        DateTime lease = (await repository.TryClaimForProcessing(message.Id, claimedAt))!.Value;
        message = await ReadAsync(message.Id);
        await Assert.That((await ReadAsync(message.Id)).Status).IsEqualTo(OutboxMessageStatus.Processing);

        string failedTag = CacheTags.EventLocationsByEvent(identity.EventId);
        var cache = new RecordingHybridCache(failedTag, failures: 1);
        await using var consumer = rows.Ordinary(identity.TenantId);
        var planner = new RecordingLocationPrivacyCorrectionPlanner();
        var dispatcher = new LocationPrivacyCorrectionDispatcher(
            cache,
            planner,
            rows.Services.GetRequiredService<EventLocationPrivacyMetrics>(),
            new LocationPrivacyCorrectionReceiptRepository(consumer, new OutboxClock(claimedAt)));
        await Assert.That(async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();

        DateTime failedAt = claimedAt.AddSeconds(1);
        OutboxFailureTransition transition = await repository.MarkAsFailed(
            message.Id,
            lease,
            "dispatch_failed",
            true,
            0,
            failedAt);
        OutboxMessage retryable = await ReadAsync(message.Id);
        await Assert.That(transition).IsEqualTo(OutboxFailureTransition.RetryScheduled);
        await Assert.That(retryable.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(retryable.RetryCount).IsEqualTo(1);

        DateTime retryLease = (await repository.TryClaimForProcessing(
            message.Id,
            failedAt.AddSeconds(1)))!.Value;
        message = await ReadAsync(message.Id);
        await dispatcher.DispatchAsync(message);
        await dispatcher.DispatchAsync(message);
        await Assert.That(await repository.MarkAsCompleted(message.Id, retryLease)).IsTrue();
        await Assert.That(async () => await dispatcher.DispatchAsync(message)).Throws<InvalidOperationException>();

        OutboxMessage completed = await ReadAsync(message.Id);
        IReadOnlyList<string> expectedTags = ExpectedTags(identity);
        await Assert.That(completed.Status).IsEqualTo(OutboxMessageStatus.Completed);
        await Assert.That(completed.ProcessedAt).IsNotNull();
        await Assert.That(cache.Effects).IsEquivalentTo(expectedTags);
        await Assert.That(cache.Attempts.Count(tag => tag == CacheTags.EventLocations)).IsEqualTo(3);
        await Assert.That(planner.Inputs.Count).IsEqualTo(2);
        await Assert.That(planner.Inputs.All(input => input.CorrectionId == message.Id
            && input.TenantId == identity.TenantId && input.EventId == identity.EventId)).IsTrue();
    }

    [Test]
    public async Task TerminalCorrection_RemainsVisibleAndReconcilesWithFencedLease()
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(fixture, instance: true);
        CorrectionIdentity identity = new(rows.A.TenantId, rows.A.EventId, rows.A.Id);
        await ProduceAsync(rows);
        await using var context = fixture.CreateDbContext();
        var repository = new OutboxRepository(context);
        OutboxMessage deadLetterMessage = await context.OutboxMessages.SingleAsync(row =>
            row.AggregateId == rows.A.Id
            && row.EventType == LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType);
        deadLetterMessage.MaxRetries = 1;
        await context.SaveChangesAsync();
        OutboxMessage failedMessage = await context.OutboxMessages.AsNoTracking().SingleAsync(row =>
            row.AggregateId == rows.B.Id
            && row.EventType == LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType);

        DateTime claimedAt = DateTime.UtcNow;
        DateTime deadLetterLease = (await repository.TryClaimForProcessing(
            deadLetterMessage.Id,
            claimedAt))!.Value;
        deadLetterMessage = await ReadAsync(deadLetterMessage.Id);
        string failedTag = CacheTags.EventLocationsByEvent(identity.EventId);
        var cache = new RecordingHybridCache(failedTag, failures: 2);
        await using var consumer = rows.Ordinary(identity.TenantId);
        var planner = new RecordingLocationPrivacyCorrectionPlanner();
        var dispatcher = new LocationPrivacyCorrectionDispatcher(
            cache,
            planner,
            rows.Services.GetRequiredService<EventLocationPrivacyMetrics>(),
            new LocationPrivacyCorrectionReceiptRepository(consumer, new OutboxClock(claimedAt)));
        await Assert.That(async () => await dispatcher.DispatchAsync(deadLetterMessage))
            .Throws<InvalidOperationException>();

        OutboxFailureTransition terminal = await repository.MarkAsFailed(
            deadLetterMessage.Id,
            deadLetterLease,
            "dispatch_failed",
            true,
            0,
            claimedAt.AddSeconds(1));
        await Assert.That(terminal).IsEqualTo(OutboxFailureTransition.DeadLettered);
        await Assert.That(async () => await dispatcher.DispatchAsync(deadLetterMessage))
            .Throws<InvalidOperationException>();

        DateTime recoveryLease = (await repository.TryClaimDeadLetterReconciliation(
            deadLetterMessage.Id,
            deadLetterLease))!.Value;
        deadLetterMessage = await ReadAsync(deadLetterMessage.Id);
        await Assert.That(async () => await dispatcher.ReconcileDeadLetterAsync(deadLetterMessage))
            .Throws<InvalidOperationException>();
        await dispatcher.ReconcileDeadLetterAsync(deadLetterMessage);
        await Assert.That(await repository.MarkDeadLetterReconciled(
            deadLetterMessage.Id,
            deadLetterLease)).IsFalse();
        await Assert.That(await repository.MarkDeadLetterReconciled(
            deadLetterMessage.Id,
            recoveryLease)).IsTrue();

        DateTime failedLease = (await repository.TryClaimForProcessing(
            failedMessage.Id,
            claimedAt))!.Value;
        OutboxFailureTransition failed = await repository.MarkAsFailed(
            failedMessage.Id,
            failedLease,
            "permanent_failure",
            false,
            0,
            claimedAt.AddSeconds(1));
        List<OutboxMessage> visibleFailures = await repository.GetFailedEntries();
        OutboxMessage deadLettered = await ReadAsync(deadLetterMessage.Id);
        OutboxMessage permanentlyFailed = await ReadAsync(failedMessage.Id);

        await Assert.That(failed).IsEqualTo(OutboxFailureTransition.Failed);
        await Assert.That(deadLettered.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(deadLettered.DeadLetteredAt).IsNotNull();
        await Assert.That(deadLettered.NextRetryAt).IsNull();
        await Assert.That(permanentlyFailed.Status).IsEqualTo(OutboxMessageStatus.Failed);
        await Assert.That(visibleFailures.Select(item => item.Id))
            .Contains(deadLetterMessage.Id)
            .And.Contains(failedMessage.Id);
        await Assert.That(cache.Effects).IsEquivalentTo(ExpectedTags(identity));
        await Assert.That(planner.Inputs).HasSingleItem();
        await Assert.That(planner.Inputs[0].CorrectionId).IsEqualTo(deadLetterMessage.Id);
        await Assert.That(planner.Inputs[0].TenantId).IsEqualTo(identity.TenantId);
        await Assert.That(planner.Inputs[0].EventId).IsEqualTo(identity.EventId);
    }

    private async Task<OutboxMessage> ReadAsync(Guid id)
    {
        await using var context = fixture.CreateDbContext();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private static async Task ProduceAsync(LocationSettingsNativeFixture rows)
    {
        var result = await rows.ApplyAsync();
        await Assert.That(result.Accepted).IsTrue();
        await rows.AssertStateAsync([rows.A.Id, rows.B.Id], committed: true);
    }

    private static IReadOnlyList<string> ExpectedTags(CorrectionIdentity identity) =>
    [
        CacheTags.EventLocations,
        CacheTags.Events,
        CacheTags.EventLists,
        CacheTags.EventDetails,
        CacheTags.EventLocationsByTenant(identity.TenantId),
        CacheTags.EventListByTenant(identity.TenantId),
        CacheTags.Event(identity.EventId),
        CacheTags.EventLocationsByEvent(identity.EventId),
        CacheTags.EventLocation(identity.EventLocationId)
    ];

    private sealed record CorrectionIdentity(Guid TenantId, Guid EventId, Guid EventLocationId);

    private sealed class OutboxClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class RecordingLocationPrivacyCorrectionPlanner : IAtprotoLocationPrivacyCorrectionPlanner
    {
        internal List<AtprotoLocationPrivacyCorrectionInput> Inputs { get; } = [];
        public Task<AtprotoPublicationPlanningResult> PlanLocationPrivacyCorrectionAsync(
            AtprotoLocationPrivacyCorrectionInput correction,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Add(correction);
            return Task.FromResult(AtprotoPublicationPlanningResult.Skipped("correction_already_planned"));
        }
    }

    private sealed class RecordingHybridCache(
        string failedTag,
        int failures) : HybridCache
    {
        private int failuresRemaining = failures;

        public List<string> Attempts { get; } = [];
        public HashSet<string> Effects { get; } = [];

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => factory(state, cancellationToken);

        public override ValueTask RemoveAsync(
            string key,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(
            string tag,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts.Add(tag);
            if (tag == failedTag && failuresRemaining > 0)
            {
                failuresRemaining--;
                throw new InvalidOperationException("Injected correction failure.");
            }

            Effects.Add(tag);
            return ValueTask.CompletedTask;
        }

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
