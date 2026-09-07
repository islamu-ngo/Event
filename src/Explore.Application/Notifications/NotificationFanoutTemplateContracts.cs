using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Explore.Application.Notifications;

[JsonConverter(typeof(JsonStringEnumConverter<NotificationFanoutChangeField>))]
public enum NotificationFanoutChangeField
{
    Cancelled = 1,
    StartTime = 3,
    EndTime = 4,
    Timezone = 5,
    Location = 6,
    Room = 7
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NotificationFanoutChangeSetV1
{
    [JsonConstructor]
    public NotificationFanoutChangeSetV1()
    {
    }

    public NotificationFanoutChangeSetV1(NotificationFanoutChangeField[] Fields) =>
        this.Fields = Fields.ToImmutableArray();

    public ImmutableArray<NotificationFanoutChangeField> Fields { get; init; } =
        ImmutableArray<NotificationFanoutChangeField>.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NotificationFanoutSnapshotV1
{
    [JsonConstructor]
    public NotificationFanoutSnapshotV1()
    {
    }

    public NotificationFanoutSnapshotV1(
        string EventTitle,
        string? SessionTitle,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        string? Timezone,
        NotificationFanoutLocationSnapshotV1? Location,
        NotificationFanoutSessionDisplayTimeV1[]? SessionDisplayTimes = null)
    {
        this.EventTitle = EventTitle;
        this.SessionTitle = SessionTitle;
        this.StartsAt = StartsAt;
        this.EndsAt = EndsAt;
        this.Timezone = Timezone;
        this.Location = Location;
        this.SessionDisplayTimes = SessionDisplayTimes?.ToImmutableList();
    }

    public string EventTitle { get; init; }
    public string? SessionTitle { get; init; }
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }
    public string? Timezone { get; init; }
    public NotificationFanoutLocationSnapshotV1? Location { get; init; }

    public ImmutableList<NotificationFanoutSessionDisplayTimeV1>? SessionDisplayTimes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NotificationFanoutSessionDisplayTimeV1(
    Guid SessionId,
    string? SessionTitle,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NotificationFanoutLocationSnapshotV1(
    Guid EventLocationId,
    Guid? RoomId,
    string? Country,
    string? City,
    string? VenueName,
    string? RoomName,
    string? StreetAddress,
    string? Postcode);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(NotificationFanoutChangeSetV1))]
[JsonSerializable(typeof(NotificationFanoutSnapshotV1))]
[JsonSerializable(typeof(NotificationFanoutSessionDisplayTimeV1))]
[JsonSerializable(typeof(NotificationFanoutLocationSnapshotV1))]
public sealed partial class NotificationFanoutTemplateJsonContext : JsonSerializerContext;

public static class NotificationFanoutTemplateJson
{
    public static string Serialize(NotificationFanoutChangeSetV1 value) =>
        JsonSerializer.Serialize(Canonicalize(value), NotificationFanoutTemplateJsonContext.Default.NotificationFanoutChangeSetV1);

    public static string Serialize(NotificationFanoutSnapshotV1 value) =>
        JsonSerializer.Serialize(Canonicalize(value), NotificationFanoutTemplateJsonContext.Default.NotificationFanoutSnapshotV1);

    public static NotificationFanoutChangeSetV1 Canonicalize(NotificationFanoutChangeSetV1 value) =>
        value with
        {
            Fields = value.Fields.OrderBy(field => (int)field).ToImmutableArray()
        };

    public static NotificationFanoutSnapshotV1 Canonicalize(NotificationFanoutSnapshotV1 value) =>
        value.SessionDisplayTimes is null
            ? value
            : value with
            {
                SessionDisplayTimes = value.SessionDisplayTimes
                    .OrderBy(session => session?.SessionId ?? Guid.Empty)
                    .ToImmutableList()
            };
}
