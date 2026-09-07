using System.Collections.Immutable;

namespace Explore.Application.Contracts.LocationPrivacy;

public sealed record EventLocationDisclosureValues(
    string? Country = null,
    string? Timezone = null,
    string? City = null,
    string? VenueName = null,
    string? RoomName = null,
    string? RoomDescription = null,
    string? StreetAddress = null,
    string? Postcode = null,
    double? Latitude = null,
    double? Longitude = null,
    string? FormattedAddress = null,
    string? MapUrl = null,
    string? Geohash = null)
{
    internal ImmutableArray<EventLocationDisclosureField> GetPresentFields()
    {
        var fields = ImmutableArray.CreateBuilder<EventLocationDisclosureField>();
        AddIfPresent(fields, Country, EventLocationDisclosureField.Country);
        AddIfPresent(fields, Timezone, EventLocationDisclosureField.Timezone);
        AddIfPresent(fields, City, EventLocationDisclosureField.City);
        AddIfPresent(fields, VenueName, EventLocationDisclosureField.VenueName);
        AddIfPresent(fields, RoomName, EventLocationDisclosureField.RoomName);
        AddIfPresent(fields, RoomDescription, EventLocationDisclosureField.RoomDescription);
        AddIfPresent(fields, StreetAddress, EventLocationDisclosureField.StreetAddress);
        AddIfPresent(fields, Postcode, EventLocationDisclosureField.Postcode);
        AddIfPresent(fields, Latitude, EventLocationDisclosureField.Latitude);
        AddIfPresent(fields, Longitude, EventLocationDisclosureField.Longitude);
        AddIfPresent(fields, FormattedAddress, EventLocationDisclosureField.FormattedAddress);
        AddIfPresent(fields, MapUrl, EventLocationDisclosureField.MapUrl);
        AddIfPresent(fields, Geohash, EventLocationDisclosureField.Geohash);
        return fields.ToImmutable();
    }

    private static void AddIfPresent<T>(
        ImmutableArray<EventLocationDisclosureField>.Builder fields,
        T? value,
        EventLocationDisclosureField field)
    {
        if (value is not null)
        {
            fields.Add(field);
        }
    }
}

public sealed record EventLocationDisclosureResult
{
    private EventLocationDisclosureResult(
        Guid eventLocationId,
        EventLocationDisclosurePurpose purpose,
        EventLocationDisclosureState state,
        Guid? locationId,
        EventLocationDisclosureValues? values,
        ImmutableArray<EventLocationDisclosureField> disclosedFields)
    {
        EventLocationId = eventLocationId;
        Purpose = purpose;
        State = state;
        LocationId = locationId;
        Values = values;
        DisclosedFields = disclosedFields;
    }

    public Guid EventLocationId { get; }
    public EventLocationDisclosurePurpose Purpose { get; }
    public EventLocationDisclosureState State { get; }
    public Guid? LocationId { get; }
    public EventLocationDisclosureValues? Values { get; }
    public ImmutableArray<EventLocationDisclosureField> DisclosedFields { get; }

    public static EventLocationDisclosureResult Suppressed(
        Guid eventLocationId,
        EventLocationDisclosurePurpose purpose,
        EventLocationDisclosureState state)
    {
        RequireEventLocationId(eventLocationId);
        if (state is not EventLocationDisclosureState.Hidden
            and not EventLocationDisclosureState.ToBeAnnounced
            and not EventLocationDisclosureState.Unavailable
            and not EventLocationDisclosureState.NeedsPrivacyReview)
        {
            throw new ArgumentException("Only non-value disclosure states can create a suppressed result.", nameof(state));
        }

        return new(eventLocationId, purpose, state, null, null, []);
    }

    internal static EventLocationDisclosureResult HiddenForInvalidRequest(
        Guid eventLocationId,
        EventLocationDisclosurePurpose purpose)
        => new(
            eventLocationId,
            purpose,
            EventLocationDisclosureState.Hidden,
            locationId: null,
            values: null,
            disclosedFields: []);

    public static EventLocationDisclosureResult Public(
        Guid eventLocationId,
        EventLocationDisclosureState state,
        EventLocationDisclosureValues values)
        => Materialize(eventLocationId, EventLocationDisclosurePurpose.Public, state, null, values);

    public static EventLocationDisclosureResult Attendee(
        Guid eventLocationId,
        EventLocationDisclosureState state,
        EventLocationDisclosureValues values)
        => Materialize(eventLocationId, EventLocationDisclosurePurpose.Attendee, state, null, values);

    public static EventLocationDisclosureResult Management(
        Guid eventLocationId,
        EventLocationDisclosureState state,
        EventLocationDisclosureValues values)
        => Materialize(eventLocationId, EventLocationDisclosurePurpose.Management, state, null, values);

    private static EventLocationDisclosureResult Materialize(
        Guid eventLocationId,
        EventLocationDisclosurePurpose purpose,
        EventLocationDisclosureState state,
        Guid? locationId,
        EventLocationDisclosureValues values)
    {
        RequireEventLocationId(eventLocationId);
        ArgumentNullException.ThrowIfNull(values);
        if (state is not EventLocationDisclosureState.Available
            and not EventLocationDisclosureState.PrivateVenue)
        {
            throw new ArgumentException("Location values require an available disclosure state.", nameof(state));
        }

        var disclosedFields = values.GetPresentFields();
        if (disclosedFields.IsEmpty)
        {
            throw new ArgumentException("An available disclosure result requires at least one selected value.", nameof(values));
        }

        foreach (var field in disclosedFields)
        {
            if (!EventLocationDisclosureContract.IsWithinPurposeCeiling(purpose, field)
                || !EventLocationDisclosureContract.HasCurrentlySatisfiablePolicyGate(field)
                || !EventLocationDisclosureContract.HasRequiredSourceAuthority(field, disclosedFields))
            {
                throw new ArgumentException($"Field {field} cannot be disclosed for purpose {purpose}.", nameof(values));
            }
        }

        if (purpose == EventLocationDisclosurePurpose.Public
            && state == EventLocationDisclosureState.PrivateVenue
            && (disclosedFields.Length != 1
                || disclosedFields[0] != EventLocationDisclosureField.VenueName
                || !string.Equals(
                    values.VenueName,
                    EventLocationDisclosureContract.PrivateHomePublicLabel,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Public Private Venue disclosure is limited to the generic venue label.",
                nameof(values));
        }

        return new(eventLocationId, purpose, state, locationId, values, disclosedFields);
    }

    private static void RequireEventLocationId(Guid eventLocationId)
    {
        if (eventLocationId == Guid.Empty)
        {
            throw new ArgumentException("EventLocation id is required.", nameof(eventLocationId));
        }
    }
}
