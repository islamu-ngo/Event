using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.LocationPrivacy;

public enum EventLocationDisclosurePurpose
{
    Public = 1,
    Attendee = 2,
    Management = 3
}

[JsonConverter(typeof(EventLocationDisclosureStateJsonConverter))]
public enum EventLocationDisclosureState
{
    Hidden = 1,
    ToBeAnnounced = 2,
    Available = 3,
    PrivateVenue = 4,
    Unavailable = 5,
    NeedsPrivacyReview = 6
}

/// <summary>
/// Single source of truth for the stable snake_case identifiers used for EventLocation disclosure
/// purposes and states on every external surface: JSON contracts, OpenAPI schemas, and metric tags.
/// </summary>
public static class EventLocationDisclosureWireNames
{
    private static readonly FrozenDictionary<EventLocationDisclosureState, string> StateNames =
        new Dictionary<EventLocationDisclosureState, string>
        {
            [EventLocationDisclosureState.Hidden] = "hidden",
            [EventLocationDisclosureState.ToBeAnnounced] = "to_be_announced",
            [EventLocationDisclosureState.Available] = "available",
            [EventLocationDisclosureState.PrivateVenue] = "private_venue",
            [EventLocationDisclosureState.Unavailable] = "unavailable",
            [EventLocationDisclosureState.NeedsPrivacyReview] = "needs_privacy_review"
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, EventLocationDisclosureState> StatesByName =
        StateNames.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    private static readonly FrozenDictionary<EventLocationDisclosurePurpose, string> PurposeNames =
        new Dictionary<EventLocationDisclosurePurpose, string>
        {
            [EventLocationDisclosurePurpose.Public] = "public",
            [EventLocationDisclosurePurpose.Attendee] = "attendee",
            [EventLocationDisclosurePurpose.Management] = "management"
        }.ToFrozenDictionary();

    public static bool TryGetStateName(EventLocationDisclosureState state, out string wireName) =>
        StateNames.TryGetValue(state, out wireName!);

    public static bool TryGetState(string wireName, out EventLocationDisclosureState state) =>
        StatesByName.TryGetValue(wireName, out state);

    public static string ForState(EventLocationDisclosureState state) =>
        StateNames.TryGetValue(state, out string? wireName)
            ? wireName
            : throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown EventLocation disclosure state.");

    public static string ForPurpose(EventLocationDisclosurePurpose purpose) =>
        PurposeNames.TryGetValue(purpose, out string? wireName)
            ? wireName
            : throw new ArgumentOutOfRangeException(
                nameof(purpose),
                purpose,
                "Unknown EventLocation disclosure purpose.");
}

public sealed class EventLocationDisclosureStateJsonConverter : JsonConverter<EventLocationDisclosureState>
{
    public override EventLocationDisclosureState Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("EventLocation disclosure state must be a known string value.");
        }

        if (reader.GetString() is not { } wireName
            || !EventLocationDisclosureWireNames.TryGetState(wireName, out var state))
        {
            throw new JsonException("Unknown EventLocation disclosure state.");
        }

        return state;
    }

    public override void Write(
        Utf8JsonWriter writer,
        EventLocationDisclosureState value,
        JsonSerializerOptions options)
    {
        if (!EventLocationDisclosureWireNames.TryGetStateName(value, out string wireName))
        {
            throw new JsonException("Unknown EventLocation disclosure state.");
        }

        writer.WriteStringValue(wireName);
    }
}

public enum EventLocationDisclosureFieldClass
{
    Baseline = 1,
    ContextSensitive = 2,
    ManagementOnly = 3,
    ExactSensitive = 4,
    ExactSensitiveDerivative = 5,
    RestrictedOperationalSecret = 6
}

public enum EventLocationDisclosureField
{
    Country = 1,
    Timezone = 2,
    City = 3,
    VenueName = 4,
    RoomName = 5,
    RoomDescription = 6,
    StreetAddress = 7,
    Postcode = 8,
    Latitude = 9,
    Longitude = 10,
    FormattedAddress = 11,
    MapUrl = 12,
    Geohash = 13,
    AccessInstructions = 14,
    EntryDetails = 15,
    DoorCode = 16
}

public enum EventLocationDisclosurePolicyGate
{
    PersistedEventLocationSelection = 1,
    UnavailableUntilExplicitTimezonePolicy = 2,
    ManagementAuthorization = 3,
    DerivedFromSourcePolicy = 4,
    SeparateOperationalContract = 5
}

public sealed record EventLocationDisclosureFieldVector(
    EventLocationDisclosureField Field,
    EventLocationDisclosureFieldClass FieldClass,
    EventLocationDisclosurePolicyGate PolicyGate,
    EventLocationDisclosureFields? PolicySelection,
    EventLocationDisclosureFields? SourceAuthoritySelection,
    IReadOnlySet<EventLocationDisclosureField> RequiredSourceFields,
    IReadOnlySet<EventLocationDisclosurePurpose> PurposeCeilings);

public static class EventLocationDisclosureContract
{
    public const string PrivateHomePublicLabel = Explore.Domain.Location.ErasedPrivateVenueLabel;

    private static readonly FrozenSet<EventLocationDisclosurePurpose> AllRoutePurposes =
        Enum.GetValues<EventLocationDisclosurePurpose>().ToFrozenSet();

    private static readonly FrozenSet<EventLocationDisclosurePurpose> ManagementOnly =
        new[] { EventLocationDisclosurePurpose.Management }.ToFrozenSet();

    private static readonly FrozenSet<EventLocationDisclosurePurpose> NoRoutePurpose =
        Array.Empty<EventLocationDisclosurePurpose>().ToFrozenSet();

    private static readonly FrozenSet<EventLocationDisclosureField> NoRequiredSourceFields =
        Array.Empty<EventLocationDisclosureField>().ToFrozenSet();

    private static readonly FrozenDictionary<EventLocationDisclosureField, EventLocationDisclosureFieldVector> Vectors =
        new[]
        {
            Selected(EventLocationDisclosureField.Country, EventLocationDisclosureFieldClass.Baseline, EventLocationDisclosureFields.Country),
            Vector(EventLocationDisclosureField.Timezone, EventLocationDisclosureFieldClass.Baseline, EventLocationDisclosurePolicyGate.UnavailableUntilExplicitTimezonePolicy, null, null, NoRequiredSourceFields, AllRoutePurposes),
            Selected(EventLocationDisclosureField.City, EventLocationDisclosureFieldClass.ContextSensitive, EventLocationDisclosureFields.City),
            Selected(EventLocationDisclosureField.VenueName, EventLocationDisclosureFieldClass.ContextSensitive, EventLocationDisclosureFields.VenueName),
            Selected(EventLocationDisclosureField.RoomName, EventLocationDisclosureFieldClass.ContextSensitive, EventLocationDisclosureFields.RoomName),
            Vector(EventLocationDisclosureField.RoomDescription, EventLocationDisclosureFieldClass.ManagementOnly, EventLocationDisclosurePolicyGate.ManagementAuthorization, null, null, NoRequiredSourceFields, ManagementOnly),
            Selected(EventLocationDisclosureField.StreetAddress, EventLocationDisclosureFieldClass.ExactSensitive, EventLocationDisclosureFields.StreetAddress),
            Selected(EventLocationDisclosureField.Postcode, EventLocationDisclosureFieldClass.ExactSensitive, EventLocationDisclosureFields.Postcode),
            Selected(EventLocationDisclosureField.Latitude, EventLocationDisclosureFieldClass.ExactSensitive, EventLocationDisclosureFields.Coordinates),
            Selected(EventLocationDisclosureField.Longitude, EventLocationDisclosureFieldClass.ExactSensitive, EventLocationDisclosureFields.Coordinates),
            Derived(EventLocationDisclosureField.FormattedAddress, EventLocationDisclosureFields.StreetAddress),
            Derived(EventLocationDisclosureField.MapUrl, EventLocationDisclosureFields.Coordinates),
            Derived(EventLocationDisclosureField.Geohash, EventLocationDisclosureFields.Coordinates),
            Operational(EventLocationDisclosureField.AccessInstructions),
            Operational(EventLocationDisclosureField.EntryDetails),
            Operational(EventLocationDisclosureField.DoorCode)
        }.ToFrozenDictionary(vector => vector.Field);

    public static IReadOnlyDictionary<EventLocationDisclosureField, EventLocationDisclosureFieldVector> FieldVectors => Vectors;

    public static bool IsWithinPurposeCeiling(
        EventLocationDisclosurePurpose purpose,
        EventLocationDisclosureField field)
        => Vectors.TryGetValue(field, out var vector)
            && vector.PurposeCeilings.Contains(purpose);

    public static bool HasCurrentlySatisfiablePolicyGate(EventLocationDisclosureField field)
        => Vectors.TryGetValue(field, out var vector)
            && vector.PolicyGate != EventLocationDisclosurePolicyGate.UnavailableUntilExplicitTimezonePolicy;

    public static bool HasRequiredSourceAuthority(
        EventLocationDisclosureField field,
        IReadOnlyCollection<EventLocationDisclosureField> presentFields)
    {
        ArgumentNullException.ThrowIfNull(presentFields);
        return Vectors.TryGetValue(field, out var vector)
            && vector.RequiredSourceFields.All(presentFields.Contains);
    }

    private static EventLocationDisclosureFieldVector Selected(
        EventLocationDisclosureField field,
        EventLocationDisclosureFieldClass fieldClass,
        EventLocationDisclosureFields policySelection)
        => Vector(
            field,
            fieldClass,
            EventLocationDisclosurePolicyGate.PersistedEventLocationSelection,
            policySelection,
            null,
            NoRequiredSourceFields,
            AllRoutePurposes);

    private static EventLocationDisclosureFieldVector Derived(
        EventLocationDisclosureField field,
        EventLocationDisclosureFields sourceAuthoritySelection)
        => Vector(
            field,
            EventLocationDisclosureFieldClass.ExactSensitiveDerivative,
            EventLocationDisclosurePolicyGate.DerivedFromSourcePolicy,
            null,
            sourceAuthoritySelection,
            RequiredSourceFields(sourceAuthoritySelection),
            AllRoutePurposes);

    private static EventLocationDisclosureFieldVector Operational(EventLocationDisclosureField field)
        => Vector(
            field,
            EventLocationDisclosureFieldClass.RestrictedOperationalSecret,
            EventLocationDisclosurePolicyGate.SeparateOperationalContract,
            null,
            null,
            NoRequiredSourceFields,
            NoRoutePurpose);

    private static IReadOnlySet<EventLocationDisclosureField> RequiredSourceFields(
        EventLocationDisclosureFields sourceAuthoritySelection)
        => sourceAuthoritySelection switch
        {
            EventLocationDisclosureFields.StreetAddress =>
                new[] { EventLocationDisclosureField.StreetAddress }.ToFrozenSet(),
            EventLocationDisclosureFields.Coordinates =>
                new[]
                {
                    EventLocationDisclosureField.Latitude,
                    EventLocationDisclosureField.Longitude
                }.ToFrozenSet(),
            _ => NoRequiredSourceFields
        };

    private static EventLocationDisclosureFieldVector Vector(
        EventLocationDisclosureField field,
        EventLocationDisclosureFieldClass fieldClass,
        EventLocationDisclosurePolicyGate policyGate,
        EventLocationDisclosureFields? policySelection,
        EventLocationDisclosureFields? sourceAuthoritySelection,
        IReadOnlySet<EventLocationDisclosureField> requiredSourceFields,
        IReadOnlySet<EventLocationDisclosurePurpose> purposes)
        => new(
            field,
            fieldClass,
            policyGate,
            policySelection,
            sourceAuthoritySelection,
            requiredSourceFields,
            purposes);
}
