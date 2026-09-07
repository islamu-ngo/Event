using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Models.Events;

/// <summary>
/// Normalizes the public and attendee disclosure shapes into one read-only view for UI components.
/// Both DTOs are already purpose-evaluated by the server, so this type only reads what it is given —
/// there is deliberately no constructor that accepts raw venue data.
/// </summary>
public sealed record EventLocationDisclosureView
{
    private EventLocationDisclosureView(
        Guid eventLocationId,
        EventLocationDisclosureState state,
        bool isAttendeeView,
        string? country,
        string? timezone,
        string? city,
        string? venueName,
        string? roomName,
        string? streetAddress,
        string? postcode,
        double? latitude,
        double? longitude,
        string? formattedAddress,
        string? mapUrl)
    {
        EventLocationId = eventLocationId;
        State = state;
        IsAttendeeView = isAttendeeView;
        Country = country;
        Timezone = timezone;
        City = city;
        VenueName = venueName;
        RoomName = roomName;
        StreetAddress = streetAddress;
        Postcode = postcode;
        Latitude = latitude;
        Longitude = longitude;
        FormattedAddress = formattedAddress;
        MapUrl = mapUrl;
    }

    public Guid EventLocationId { get; }
    public EventLocationDisclosureState State { get; }

    /// <summary>True when this view came from the authenticated attendee surface.</summary>
    public bool IsAttendeeView { get; }

    public string? Country { get; }
    public string? Timezone { get; }
    public string? City { get; }
    public string? VenueName { get; }
    public string? RoomName { get; }
    public string? StreetAddress { get; }
    public string? Postcode { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }
    public string? FormattedAddress { get; }
    public string? MapUrl { get; }

    public bool IsToBeAnnounced => State == EventLocationDisclosureState.To_be_announced;

    public bool IsPrivateVenue => State == EventLocationDisclosureState.Private_venue;

    public bool IsUnavailable => State == EventLocationDisclosureState.Unavailable
        || State == EventLocationDisclosureState.Needs_privacy_review;

    /// <summary>True when the server released at least one renderable field.</summary>
    public bool HasAnyDetail =>
        !string.IsNullOrWhiteSpace(VenueName)
        || !string.IsNullOrWhiteSpace(City)
        || !string.IsNullOrWhiteSpace(Country)
        || !string.IsNullOrWhiteSpace(RoomName)
        || !string.IsNullOrWhiteSpace(StreetAddress)
        || !string.IsNullOrWhiteSpace(Postcode)
        || Latitude.HasValue && Longitude.HasValue;

    /// <summary>True only when the caller may see exact-sensitive data the public surface never carries.</summary>
    public bool HasExactDetail =>
        !string.IsNullOrWhiteSpace(StreetAddress)
        || !string.IsNullOrWhiteSpace(Postcode)
        || Latitude.HasValue && Longitude.HasValue;

    public static EventLocationDisclosureView FromPublic(EventLocationPublicDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        EventLocationPublicFieldsDto? fields = dto.Fields;
        return new EventLocationDisclosureView(
            dto.EventLocationId ?? Guid.Empty,
            dto.State ?? EventLocationDisclosureState.Hidden,
            isAttendeeView: false,
            fields?.Country,
            fields?.Timezone,
            fields?.City,
            fields?.VenueName,
            fields?.RoomName,
            fields?.StreetAddress,
            fields?.Postcode,
            fields?.Latitude,
            fields?.Longitude,
            fields?.FormattedAddress,
            fields?.MapUrl);
    }

    public static EventLocationDisclosureView FromAttendee(EventLocationAttendeeDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        EventLocationAttendeeFieldsDto? fields = dto.Fields;
        return new EventLocationDisclosureView(
            dto.EventLocationId ?? Guid.Empty,
            dto.State ?? EventLocationDisclosureState.Hidden,
            isAttendeeView: true,
            fields?.Country,
            fields?.Timezone,
            fields?.City,
            fields?.VenueName,
            fields?.RoomName,
            fields?.StreetAddress,
            fields?.Postcode,
            fields?.Latitude,
            fields?.Longitude,
            fields?.FormattedAddress,
            fields?.MapUrl);
    }

    /// <summary>
    /// Chooses the view a signed-in visitor should see. The attendee surface wins only when it actually
    /// carries more than the public one; a registration that grants nothing extra must not downgrade an
    /// already-visible public venue to a blank card.
    /// </summary>
    public static EventLocationDisclosureView Prefer(
        EventLocationDisclosureView? publicView,
        EventLocationDisclosureView? attendeeView)
    {
        if (attendeeView is null)
        {
            return publicView ?? throw new ArgumentNullException(nameof(publicView));
        }

        if (publicView is null)
        {
            return attendeeView;
        }

        return attendeeView.HasExactDetail || !publicView.HasAnyDetail
            ? attendeeView
            : publicView.HasAnyDetail && !attendeeView.HasAnyDetail
                ? publicView
                : attendeeView;
    }
}
