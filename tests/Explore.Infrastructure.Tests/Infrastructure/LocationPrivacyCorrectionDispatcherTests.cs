using System.Text.Json;
using Explore.Application.Caching;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using Explore.Tests.Shared.Telemetry;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category("EventLocationPrivacy")]
public sealed class LocationPrivacyCorrectionDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithLocationPiiErased_InvalidatesGlobalLocationSurfaces()
    {
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);

        await dispatcher.DispatchAsync(CreateLocationPiiErasedMessage());

        await Assert.That(cache.Effects).IsEquivalentTo(GlobalTags);
    }

    [Test]
    public async Task DispatchAsync_WithCorrectionRequested_InvalidatesGlobalAndProjectionTags()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);

        await dispatcher.DispatchAsync(CreateCorrectionRequestedMessage(identity));

        await Assert.That(cache.Effects).IsEquivalentTo(ExpectedTags(identity));
    }

    [Test]
    public async Task DispatchAsync_WithGovernanceCorrection_InvalidatesGlobalAndProjectionTags()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);

        await dispatcher.DispatchAsync(CreateGovernanceCorrectionMessage(identity));

        await Assert.That(cache.Effects).IsEquivalentTo(ExpectedTags(identity));
    }

    [Test]
    public async Task DispatchAsync_WithEventCorrection_RequestsCurrentPdsProjectionWithoutPii()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        var planner = Substitute.For<IAtprotoLocationPrivacyCorrectionPlanner>();
        OutboxMessage message = CreateCorrectionRequestedMessage(identity);
        var dispatcher = new LocationPrivacyCorrectionDispatcher(cache, planner, EventLocationPrivacyMetricsFactory.Create());
        planner.PlanLocationPrivacyCorrectionAsync(
                Arg.Any<AtprotoLocationPrivacyCorrectionInput>(),
                Arg.Any<CancellationToken>())
            .Returns(AtprotoPublicationPlanningResult.Skipped("correction_already_planned"));

        await dispatcher.DispatchAsync(message);

        await planner.Received(1).PlanLocationPrivacyCorrectionAsync(
            Arg.Is<AtprotoLocationPrivacyCorrectionInput>(input =>
                input.TenantId == identity.TenantId
                && input.EventId == identity.EventId
                && input.CorrectionId == message.Id
                && input.CreatedAtUtc == message.CreatedAt),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_WhenPdsCorrectionCannotBePlanned_RemainsRetryable()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        var planner = Substitute.For<IAtprotoLocationPrivacyCorrectionPlanner>();
        planner.PlanLocationPrivacyCorrectionAsync(
                Arg.Any<AtprotoLocationPrivacyCorrectionInput>(),
                Arg.Any<CancellationToken>())
            .Returns(AtprotoPublicationPlanningResult.Skipped("session_missing"));
        var dispatcher = new LocationPrivacyCorrectionDispatcher(cache, planner, EventLocationPrivacyMetricsFactory.Create());

        await Assert.That(async () => await dispatcher.DispatchAsync(CreateCorrectionRequestedMessage(identity)))
            .Throws<InvalidOperationException>();

        await Assert.That(cache.Effects).Contains(CacheTags.EventLocations);
        await planner.Received(1).PlanLocationPrivacyCorrectionAsync(
            Arg.Any<AtprotoLocationPrivacyCorrectionInput>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_WithGovernanceReasonMember_FailsClosedBeforeTouchingCache()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);
        OutboxMessage message = CreateMessage(
            LocationPrivacyCorrectionDispatcher.GovernanceCorrectionEventType,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                identity.TenantId,
                identity.EventId,
                identity.EventLocationId,
                PolicyVersion = 2,
                Reason = "governance_tightening"
            }),
            identity.EventLocationId);

        await Assert.That(async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await Assert.That(cache.Attempts).IsEmpty();
    }

    [Test]
    public async Task DispatchAsync_WhenDeliveredTwice_ReplaysIdempotentTagInvalidation()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);
        OutboxMessage message = CreateCorrectionRequestedMessage(identity);

        await dispatcher.DispatchAsync(message);
        await dispatcher.DispatchAsync(message);

        IReadOnlyList<string> expected = ExpectedTags(identity);
        await Assert.That(cache.Effects).IsEquivalentTo(expected);
        await Assert.That(cache.Attempts.Count).IsEqualTo(expected.Count * 2);
        await Assert.That(cache.Attempts.Count(tag => tag == CacheTags.EventLocations)).IsEqualTo(2);
    }

    [Test]
    public async Task DispatchAsync_WhenOneTagFailsOnce_RetryCompletesWithoutDuplicateEffects()
    {
        CorrectionIdentity identity = CreateIdentity();
        string failedTag = CacheTags.EventLocationsByEvent(identity.EventId);
        var cache = new RecordingHybridCache(failOnceOnTag: failedTag);
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);
        OutboxMessage message = CreateCorrectionRequestedMessage(identity);

        await Assert.That(async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await dispatcher.DispatchAsync(message);

        IReadOnlyList<string> expected = ExpectedTags(identity);
        await Assert.That(cache.Effects).IsEquivalentTo(expected);
        await Assert.That(cache.Attempts.Count(tag => tag == failedTag)).IsEqualTo(2);
        await Assert.That(cache.Attempts.Count(tag => tag == CacheTags.EventLocations)).IsEqualTo(2);
    }

    [Test]
    public async Task DispatchAsync_WithMalformedPayload_FailsClosedWithoutLeakingPayloadOrTouchingCache()
    {
        const string payloadCanary = "PRIVATE-ADDRESS-CANARY";
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);
        OutboxMessage message = CreateMessage(
            LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType,
            $"{{\"SchemaVersion\":1,\"Unexpected\":\"{payloadCanary}\"",
            Guid.CreateVersion7());

        InvalidOperationException exception = await Assert.That(
                async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();

        await Assert.That(exception.Message).DoesNotContain(payloadCanary);
        await Assert.That(cache.Attempts).IsEmpty();
    }

    [Test]
    public async Task DispatchAsync_WithUnsupportedSchema_FailsClosedBeforeTouchingCache()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);
        OutboxMessage message = CreateMessage(
            LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 2,
                IntentId = Guid.CreateVersion7(),
                AuthoritySequence = 1,
                identity.TenantId,
                identity.EventId,
                identity.EventLocationId,
                LocationId = (Guid?)null,
                PolicyVersion = 1
            }),
            identity.EventLocationId);

        await Assert.That(async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await Assert.That(cache.Attempts).IsEmpty();
    }

    [Test]
    public async Task DispatchAsync_WithMissingRequiredIdentity_FailsClosedBeforeTouchingCache()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);
        OutboxMessage message = CreateMessage(
            LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                IntentId = Guid.CreateVersion7(),
                AuthoritySequence = 1,
                TenantId = Guid.Empty,
                identity.EventId,
                identity.EventLocationId,
                LocationId = (Guid?)null,
                PolicyVersion = 1
            }),
            identity.EventLocationId);

        await Assert.That(async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await Assert.That(cache.Attempts).IsEmpty();
    }

    [Test]
    public async Task DispatchAsync_WhenCancellationArrivesMidBatch_StopsBeforeReportingSuccess()
    {
        CorrectionIdentity identity = CreateIdentity();
        using var cancellation = new CancellationTokenSource();
        var cache = new RecordingHybridCache(
            cancelAfterTag: CacheTags.EventLocations,
            cancellation: cancellation);
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);

        await Assert.That(async () => await dispatcher.DispatchAsync(
                CreateCorrectionRequestedMessage(identity),
                cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(cache.Effects).IsEquivalentTo([CacheTags.EventLocations]);
    }

    [Test]
    public async Task DispatchAsync_WithMismatchedAggregateIdentity_FailsClosedBeforeTouchingCache()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);
        OutboxMessage message = CreateCorrectionRequestedMessage(identity);
        message.AggregateId = Guid.CreateVersion7();

        await Assert.That(async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await Assert.That(cache.Attempts).IsEmpty();
    }

    [Test]
    public async Task DispatchAsync_WithUnknownPayloadMember_FailsClosedBeforeTouchingCache()
    {
        CorrectionIdentity identity = CreateIdentity();
        var cache = new RecordingHybridCache();
        LocationPrivacyCorrectionDispatcher dispatcher = CreateDispatcher(cache);
        OutboxMessage message = CreateMessage(
            LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                IntentId = Guid.CreateVersion7(),
                AuthoritySequence = 1,
                identity.TenantId,
                identity.EventId,
                identity.EventLocationId,
                LocationId = (Guid?)null,
                PolicyVersion = 1,
                InjectedVenueName = "PRIVATE-VENUE-CANARY"
            }),
            identity.EventLocationId);

        await Assert.That(async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await Assert.That(cache.Attempts).IsEmpty();
    }

    private static readonly string[] GlobalTags =
    [
        CacheTags.EventLocations,
        CacheTags.Events,
        CacheTags.EventLists,
        CacheTags.EventDetails
    ];

    private static LocationPrivacyCorrectionDispatcher CreateDispatcher(HybridCache cache)
    {
        var planner = Substitute.For<IAtprotoLocationPrivacyCorrectionPlanner>();
        planner.PlanLocationPrivacyCorrectionAsync(
                Arg.Any<AtprotoLocationPrivacyCorrectionInput>(),
                Arg.Any<CancellationToken>())
            .Returns(AtprotoPublicationPlanningResult.Skipped("correction_already_planned"));
        return new LocationPrivacyCorrectionDispatcher(cache, planner, EventLocationPrivacyMetricsFactory.Create());
    }

    private static IReadOnlyList<string> ExpectedTags(CorrectionIdentity identity) =>
    [
        .. GlobalTags,
        CacheTags.EventLocationsByTenant(identity.TenantId),
        CacheTags.EventListByTenant(identity.TenantId),
        CacheTags.Event(identity.EventId),
        CacheTags.EventLocationsByEvent(identity.EventId),
        CacheTags.EventLocation(identity.EventLocationId)
    ];

    private static OutboxMessage CreateLocationPiiErasedMessage()
    {
        Guid locationId = Guid.CreateVersion7();
        return CreateMessage(
            LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                IntentId = Guid.CreateVersion7(),
                AuthoritySequence = 1,
                LocationId = locationId,
                LocationVersion = Guid.CreateVersion7()
            }),
            locationId,
            nameof(Location));
    }

    private static OutboxMessage CreateCorrectionRequestedMessage(CorrectionIdentity identity) => CreateMessage(
        LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType,
        JsonSerializer.Serialize(new
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
        identity.EventLocationId);

    private static OutboxMessage CreateGovernanceCorrectionMessage(CorrectionIdentity identity) => CreateMessage(
        "location.privacy.corrected",
        JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            identity.TenantId,
            identity.EventId,
            identity.EventLocationId,
            PolicyVersion = 2
        }),
        identity.EventLocationId);

    private static OutboxMessage CreateMessage(
        string eventType,
        string payload,
        Guid aggregateId,
        string aggregateType = nameof(EventLocation)) => new()
        {
            Id = Guid.CreateVersion7(),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            Payload = payload,
            CreatedAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc)
        };

    private static CorrectionIdentity CreateIdentity() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7());

    private sealed record CorrectionIdentity(Guid TenantId, Guid EventId, Guid EventLocationId);

    private sealed class RecordingHybridCache(
        string? failOnceOnTag = null,
        string? cancelAfterTag = null,
        CancellationTokenSource? cancellation = null) : HybridCache
    {
        private bool hasFailed;

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
            if (!hasFailed && tag == failOnceOnTag)
            {
                hasFailed = true;
                throw new InvalidOperationException("Injected one-time cache invalidation failure.");
            }

            Effects.Add(tag);
            if (tag == cancelAfterTag)
            {
                cancellation?.Cancel();
            }

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
