using System.Text.Json;
using System.Text.Json.Serialization;

namespace Explore.Application.Models.InternalEvents;

public sealed record EventHeavyRedactedNotificationFanoutPayloadParseResult(
    EventHeavyRedactedNotificationFanoutRequested Request,
    string CanonicalPayload,
    bool WasLegacy);

public static class EventHeavyRedactedNotificationFanoutPayloadParser
{
    public const string SafeInvalidPayload = "{\"Version\":1}";

    private static readonly HashSet<string> CanonicalMembers =
    [
        nameof(EventHeavyRedactedNotificationFanoutRequested.TenantId),
        nameof(EventHeavyRedactedNotificationFanoutRequested.ModerationRecordId),
        nameof(EventHeavyRedactedNotificationFanoutRequested.Version)
    ];

    private static readonly HashSet<string> LegacyMembers =
    [
        nameof(LegacyPayload.TenantId),
        nameof(LegacyPayload.ModerationRecordId),
        nameof(LegacyPayload.SourceActorId),
        nameof(LegacyPayload.RedactedAt)
    ];

    public static EventHeavyRedactedNotificationFanoutPayloadParseResult Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw InvalidPayload();
        }

        using JsonDocument document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw InvalidPayload();
        }

        string[] members = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        if (members.Length != members.Distinct(StringComparer.Ordinal).Count())
        {
            throw InvalidPayload();
        }

        var memberSet = members.ToHashSet(StringComparer.Ordinal);
        if (memberSet.SetEquals(CanonicalMembers))
        {
            EventHeavyRedactedNotificationFanoutRequested request = JsonSerializer.Deserialize<EventHeavyRedactedNotificationFanoutRequested>(payload)
                ?? throw InvalidPayload();
            ValidateCanonical(request);
            return new(request, JsonSerializer.Serialize(request), WasLegacy: false);
        }

        if (memberSet.SetEquals(LegacyMembers))
        {
            LegacyPayload legacy = JsonSerializer.Deserialize<LegacyPayload>(payload)
                ?? throw InvalidPayload();
            if (legacy.TenantId == Guid.Empty
                || legacy.ModerationRecordId == Guid.Empty
                || legacy.SourceActorId == Guid.Empty
                || legacy.RedactedAt == default)
            {
                throw InvalidPayload();
            }

            var request = new EventHeavyRedactedNotificationFanoutRequested
            {
                TenantId = legacy.TenantId,
                ModerationRecordId = legacy.ModerationRecordId,
                Version = EventHeavyRedactedNotificationFanoutRequested.CurrentVersion
            };
            return new(request, JsonSerializer.Serialize(request), WasLegacy: true);
        }

        throw InvalidPayload();
    }

    private static void ValidateCanonical(EventHeavyRedactedNotificationFanoutRequested request)
    {
        if (request.TenantId == Guid.Empty
            || request.ModerationRecordId == Guid.Empty
            || request.Version != EventHeavyRedactedNotificationFanoutRequested.CurrentVersion)
        {
            throw InvalidPayload();
        }
    }

    private static JsonException InvalidPayload() =>
        new("The heavy moderation fanout payload is invalid.");

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record LegacyPayload
    {
        public required Guid TenantId { get; init; }
        public required Guid ModerationRecordId { get; init; }
        public required Guid SourceActorId { get; init; }
        public required DateTimeOffset RedactedAt { get; init; }
    }
}
