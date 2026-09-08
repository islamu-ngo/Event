using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Caching;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Infrastructure.Messaging;

public sealed class LocationPrivacyCorrectionDispatcher(
    HybridCache cache,
    IAtprotoLocationPrivacyCorrectionPlanner correctionPlanner,
    EventLocationPrivacyMetrics metrics)
{
    public const string GovernanceCorrectionEventType = LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType;

    private static readonly string[] GlobalTags =
    [
        CacheTags.EventLocations,
        CacheTags.Events,
        CacheTags.EventLists,
        CacheTags.EventDetails
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        ApplyAsync(message, isDeadLetterReconciliation: false, cancellationToken);

    /// <summary>
    /// Re-applies a correction the outbox already moved to dead-letter. Behaviourally identical to
    /// <see cref="DispatchAsync"/> — corrections are idempotent — but recorded as a dead-letter
    /// observation so backlog pressure stays distinguishable from ordinary retries.
    /// </summary>
    public Task ReconcileDeadLetterAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        ApplyAsync(message, isDeadLetterReconciliation: true, cancellationToken);

    private async Task ApplyAsync(
        OutboxMessage message,
        bool isDeadLetterReconciliation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsSupportedEventType(message.EventType))
        {
            // Recorded before any metric so an unroutable event type can never widen tag cardinality.
            throw Invalid(message, "event type is not a supported location-privacy correction");
        }

        if (isDeadLetterReconciliation)
        {
            metrics.RecordCorrection(message.EventType, EventLocationCorrectionOutcome.DeadLetter);
        }

        try
        {
            await ApplyCorrectionAsync(message, cancellationToken);
        }
        catch (Exception) when (!isDeadLetterReconciliation)
        {
            metrics.RecordCorrection(message.EventType, EventLocationCorrectionOutcome.Retry);
            throw;
        }

        if (!isDeadLetterReconciliation)
        {
            metrics.RecordCorrection(message.EventType, EventLocationCorrectionOutcome.Success);
        }
    }

    private async Task ApplyCorrectionAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        CorrectionDispatchPlan plan = message.EventType switch
        {
            LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType =>
                ValidateErasure(message),
            LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType =>
                ValidateCorrectionRequest(message),
            GovernanceCorrectionEventType =>
                ValidateGovernanceCorrection(message),
            _ => throw Invalid(message, "event type is not a supported location-privacy correction")
        };

        foreach (string tag in plan.Tags)
        {
            await cache.RemoveByTagAsync(tag, cancellationToken);
        }

        if (plan.ExternalCorrection is not null)
        {
            AtprotoPublicationPlanningResult result = await correctionPlanner.PlanLocationPrivacyCorrectionAsync(
                plan.ExternalCorrection,
                cancellationToken);
            if (!result.Enqueued
                && result.ReasonCode is not "correction_already_planned"
                && result.ReasonCode is not "remote_record_missing")
            {
                throw Invalid(
                    message,
                    $"PDS correction was not planned ({result.ReasonCode ?? "unknown"})");
            }
        }
    }

    private static bool IsSupportedEventType(string eventType) => eventType is
        LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
        or LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType
        or GovernanceCorrectionEventType;

    private static CorrectionDispatchPlan ValidateErasure(OutboxMessage message)
    {
        LocationPiiErasedPayload payload = Deserialize<LocationPiiErasedPayload>(message);
        if (payload.SchemaVersion != 1
            || !IsUuidVersion7(payload.IntentId)
            || payload.AuthoritySequence <= 0
            || payload.LocationId == Guid.Empty
            || payload.LocationVersion == Guid.Empty
            || !MatchesEnvelope(message, nameof(Location), payload.LocationId))
        {
            throw Invalid(message, "payload failed closed-schema validation");
        }

        return new(GlobalTags, null);
    }

    private static CorrectionDispatchPlan ValidateCorrectionRequest(OutboxMessage message)
    {
        LocationPrivacyCorrectionRequestedPayload payload =
            Deserialize<LocationPrivacyCorrectionRequestedPayload>(message);
        if (payload.SchemaVersion != 1
            || !IsUuidVersion7(payload.IntentId)
            || payload.AuthoritySequence <= 0
            || payload.TenantId == Guid.Empty
            || payload.EventId == Guid.Empty
            || payload.EventLocationId == Guid.Empty
            || payload.LocationId == Guid.Empty
            || !MatchesEnvelope(message, nameof(EventLocation), payload.EventLocationId)
            || payload.PolicyVersion <= 0
            || !HasValidCorrectionEnvelope(message))
        {
            throw Invalid(message, "payload failed closed-schema validation");
        }

        return ProjectionPlan(message, payload.TenantId, payload.EventId, payload.EventLocationId);
    }

    private static CorrectionDispatchPlan ValidateGovernanceCorrection(OutboxMessage message)
    {
        LocationPrivacyGovernanceCorrectionPayload payload =
            Deserialize<LocationPrivacyGovernanceCorrectionPayload>(message);
        if (payload.SchemaVersion != 1
            || payload.TenantId == Guid.Empty
            || payload.EventId == Guid.Empty
            || payload.EventLocationId == Guid.Empty
            || !MatchesEnvelope(message, nameof(EventLocation), payload.EventLocationId)
            || payload.PolicyVersion <= 0
            || !HasValidCorrectionEnvelope(message))
        {
            throw Invalid(message, "payload failed closed-schema validation");
        }

        return ProjectionPlan(message, payload.TenantId, payload.EventId, payload.EventLocationId);
    }

    private static CorrectionDispatchPlan ProjectionPlan(
        OutboxMessage message,
        Guid tenantId,
        Guid eventId,
        Guid eventLocationId) => new(
            ProjectionTags(tenantId, eventId, eventLocationId),
            new AtprotoLocationPrivacyCorrectionInput(
                tenantId,
                eventId,
                message.Id,
                message.CreatedAt));

    private static IReadOnlyList<string> ProjectionTags(
        Guid tenantId,
        Guid eventId,
        Guid eventLocationId) =>
    [
        .. GlobalTags,
        CacheTags.EventLocationsByTenant(tenantId),
        CacheTags.EventListByTenant(tenantId),
        CacheTags.Event(eventId),
        CacheTags.EventLocationsByEvent(eventId),
        CacheTags.EventLocation(eventLocationId)
    ];

    private static TPayload Deserialize<TPayload>(OutboxMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            throw Invalid(message, "payload is required");
        }

        try
        {
            return JsonSerializer.Deserialize<TPayload>(message.Payload, SerializerOptions)
                ?? throw Invalid(message, "payload deserialized to null");
        }
        catch (JsonException)
        {
            throw Invalid(message, "payload is malformed JSON");
        }
    }

    private static bool IsUuidVersion7(Guid value) =>
        value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;

    private static bool MatchesEnvelope(OutboxMessage message, string aggregateType, Guid aggregateId) =>
        message.AggregateId == aggregateId
        && string.Equals(message.AggregateType, aggregateType, StringComparison.Ordinal);

    private static bool HasValidCorrectionEnvelope(OutboxMessage message) =>
        IsUuidVersion7(message.Id) && message.CreatedAt.Kind == DateTimeKind.Utc;

    private static InvalidOperationException Invalid(OutboxMessage message, string reason) =>
        new($"Location-privacy outbox message {message.Id} ({message.EventType}) {reason}.");

    private sealed record LocationPiiErasedPayload(
        int SchemaVersion,
        Guid IntentId,
        long AuthoritySequence,
        Guid LocationId,
        Guid LocationVersion);

    private sealed record LocationPrivacyCorrectionRequestedPayload(
        int SchemaVersion,
        Guid IntentId,
        long AuthoritySequence,
        Guid TenantId,
        Guid EventId,
        Guid EventLocationId,
        Guid? LocationId,
        int PolicyVersion);

    private sealed record LocationPrivacyGovernanceCorrectionPayload(
        int SchemaVersion,
        Guid TenantId,
        Guid EventId,
        Guid EventLocationId,
        int PolicyVersion);

    private sealed record CorrectionDispatchPlan(
        IReadOnlyList<string> Tags,
        AtprotoLocationPrivacyCorrectionInput? ExternalCorrection);
}
