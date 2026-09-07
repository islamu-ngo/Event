using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<ProjectionTestContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("ProjectionDb")]
[Category("EventLocationPrivacy")]
public sealed class LocationPrivacyCorrectionOutboxPostgreSqlTests(ProjectionTestContainerFixture fixture)
{
    [Test]
    public async Task RetryAfterPartialCorrection_PersistsPendingProcessingAndCompletedStates()
    {
        CorrectionIdentity identity = CreateIdentity();
        OutboxMessage message = CreateMessage(identity, maxRetries: 3);
        await using var context = fixture.CreateDbContext();
        var repository = new OutboxRepository(context);
        await repository.Create(message);

        await Assert.That((await ReadAsync(message.Id)).Status).IsEqualTo(OutboxMessageStatus.Pending);

        DateTime claimedAt = DateTime.UtcNow;
        DateTime lease = (await repository.TryClaimForProcessing(message.Id, claimedAt))!.Value;
        await Assert.That((await ReadAsync(message.Id)).Status).IsEqualTo(OutboxMessageStatus.Processing);

        string failedTag = CacheTags.EventLocationsByEvent(identity.EventId);
        var cache = new RecordingHybridCache(failedTag, failures: 1);
        var dispatcher = new LocationPrivacyCorrectionDispatcher(
            cache,
            new NoopLocationPrivacyCorrectionPlanner(),
            EventLocationPrivacyMetricsFactory.Create());
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
        await dispatcher.DispatchAsync(message);
        await Assert.That(await repository.MarkAsCompleted(message.Id, retryLease)).IsTrue();
        await dispatcher.DispatchAsync(message);

        OutboxMessage completed = await ReadAsync(message.Id);
        IReadOnlyList<string> expectedTags = ExpectedTags(identity);
        await Assert.That(completed.Status).IsEqualTo(OutboxMessageStatus.Completed);
        await Assert.That(completed.ProcessedAt).IsNotNull();
        await Assert.That(cache.Effects).IsEquivalentTo(expectedTags);
        await Assert.That(cache.Attempts.Count(tag => tag == CacheTags.EventLocations)).IsEqualTo(3);
    }

    [Test]
    public async Task TerminalCorrection_RemainsVisibleAndReconcilesWithFencedLease()
    {
        CorrectionIdentity identity = CreateIdentity();
        OutboxMessage deadLetterMessage = CreateMessage(identity, maxRetries: 1);
        OutboxMessage failedMessage = CreateMessage(CreateIdentity(), maxRetries: 3);
        await using var context = fixture.CreateDbContext();
        var repository = new OutboxRepository(context);
        await repository.CreateRange([deadLetterMessage, failedMessage]);

        DateTime claimedAt = DateTime.UtcNow;
        DateTime deadLetterLease = (await repository.TryClaimForProcessing(
            deadLetterMessage.Id,
            claimedAt))!.Value;
        string failedTag = CacheTags.EventLocationsByEvent(identity.EventId);
        var cache = new RecordingHybridCache(failedTag, failures: 2);
        var dispatcher = new LocationPrivacyCorrectionDispatcher(
            cache,
            new NoopLocationPrivacyCorrectionPlanner(),
            EventLocationPrivacyMetricsFactory.Create());
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
        await dispatcher.DispatchAsync(deadLetterMessage);
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
    }

    private async Task<OutboxMessage> ReadAsync(Guid id)
    {
        await using var context = fixture.CreateDbContext();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private static OutboxMessage CreateMessage(CorrectionIdentity identity, int maxRetries) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = nameof(EventLocation),
        AggregateId = identity.EventLocationId,
        EventType = LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType,
        Payload = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            IntentId = Guid.CreateVersion7(),
            AuthoritySequence = 1,
            identity.TenantId,
            identity.EventId,
            identity.EventLocationId,
            LocationId = (Guid?)null,
            PolicyVersion = 1
        }),
        Status = OutboxMessageStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        MaxRetries = maxRetries
    };

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

    private static CorrectionIdentity CreateIdentity() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7());

    private sealed record CorrectionIdentity(Guid TenantId, Guid EventId, Guid EventLocationId);

    private sealed class NoopLocationPrivacyCorrectionPlanner : IAtprotoLocationPrivacyCorrectionPlanner
    {
        public Task<AtprotoPublicationPlanningResult> PlanLocationPrivacyCorrectionAsync(
            AtprotoLocationPrivacyCorrectionInput correction,
            CancellationToken cancellationToken) => Task.FromResult(
                AtprotoPublicationPlanningResult.Skipped("correction_already_planned"));
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
