using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed record EventLocationDisclosureGovernanceFact(
    bool IsResolved,
    bool AllowHomeLocations,
    bool AllowPublicExactAddress,
    bool AllowPublicCoordinates,
    LocationDisclosureAudienceEnum MinimumHomeAudience,
    TimeSpan DefaultRevealOffset);

public sealed record EventLocationDisclosureDerivativeValues(
    string? FormattedAddress,
    string? MapUrl,
    string? Geohash);

public enum EventLocationDisclosureAuthorityKind
{
    Public = 1,
    AttendeeRegistration = 2,
    Management = 3
}

public sealed class EventLocationDisclosureAuthorityFact
{
    private EventLocationDisclosureAuthorityFact(
        EventLocationDisclosureAuthorityKind kind,
        EventLocationDisclosurePurpose purpose,
        Guid? requesterUserId,
        Guid tenantId,
        Guid eventId,
        Guid eventLocationId,
        EventLocationRegistrationAccess? registrationAccess)
    {
        Kind = kind;
        Purpose = purpose;
        RequesterUserId = requesterUserId;
        TenantId = tenantId;
        EventId = eventId;
        EventLocationId = eventLocationId;
        RegistrationAccess = registrationAccess;
    }

    public EventLocationDisclosureAuthorityKind Kind { get; }
    public EventLocationDisclosurePurpose Purpose { get; }
    public Guid? RequesterUserId { get; }
    public Guid TenantId { get; }
    public Guid EventId { get; }
    public Guid EventLocationId { get; }
    public EventLocationRegistrationAccess? RegistrationAccess { get; }

    internal static EventLocationDisclosureAuthorityFact ForPublic(
        Guid tenantId,
        Guid eventId,
        Guid eventLocationId)
        => Create(
            EventLocationDisclosureAuthorityKind.Public,
            EventLocationDisclosurePurpose.Public,
            null,
            tenantId,
            eventId,
            eventLocationId,
            null);

    internal static EventLocationDisclosureAuthorityFact ForAttendee(
        Guid requesterUserId,
        Guid tenantId,
        Guid eventId,
        Guid eventLocationId,
        EventLocationRegistrationAccess registrationAccess)
    {
        ArgumentNullException.ThrowIfNull(registrationAccess);
        if (registrationAccess.EventId != eventId
            || registrationAccess.RequestedEventLocationId != eventLocationId)
        {
            throw new ArgumentException(
                "Registration access must match the scoped EventLocation authority.",
                nameof(registrationAccess));
        }

        return Create(
            EventLocationDisclosureAuthorityKind.AttendeeRegistration,
            EventLocationDisclosurePurpose.Attendee,
            requesterUserId,
            tenantId,
            eventId,
            eventLocationId,
            registrationAccess);
    }

    internal static EventLocationDisclosureAuthorityFact ForManagement(
        Guid requesterUserId,
        Guid tenantId,
        Guid eventId,
        Guid eventLocationId)
        => Create(
            EventLocationDisclosureAuthorityKind.Management,
            EventLocationDisclosurePurpose.Management,
            requesterUserId,
            tenantId,
            eventId,
            eventLocationId,
            null);

    private static EventLocationDisclosureAuthorityFact Create(
        EventLocationDisclosureAuthorityKind kind,
        EventLocationDisclosurePurpose purpose,
        Guid? requesterUserId,
        Guid tenantId,
        Guid eventId,
        Guid eventLocationId,
        EventLocationRegistrationAccess? registrationAccess)
    {
        if (tenantId == Guid.Empty
            || eventId == Guid.Empty
            || eventLocationId == Guid.Empty
            || requesterUserId == Guid.Empty)
        {
            throw new ArgumentException("Scoped disclosure authority requires non-empty identifiers.");
        }

        return new(
            kind,
            purpose,
            requesterUserId,
            tenantId,
            eventId,
            eventLocationId,
            registrationAccess);
    }
}

public sealed record EventLocationDisclosureEvaluationFacts(
    EventLocationDisclosureRequest Request,
    EventLocation? EventLocation,
    Location? Location,
    LocationRoom? Room,
    EventLocationDisclosureGovernanceFact Governance,
    EventLocationDisclosureAuthorityFact? Authority,
    DateTimeOffset ServerNowUtc,
    EventLocationDisclosureDerivativeValues? Derivatives);

public sealed class EventLocationDisclosureEvaluator
{
    public EventLocationDisclosureResult Evaluate(EventLocationDisclosureEvaluationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.Request);

        var request = facts.Request;
        if (facts.Governance is null
            || !HasValidRequest(request)
            || !HasValidAssociation(request, facts.EventLocation)
            || !HasValidGovernance(facts.Governance))
        {
            return Hidden(request);
        }

        var eventLocation = facts.EventLocation!;
        if (!HasPurposeAuthority(request, eventLocation, facts)
            || !HasValidServerTime(facts.ServerNowUtc)
            || !HasValidDisclosurePolicy(eventLocation, facts.Governance.DefaultRevealOffset))
        {
            return Hidden(request);
        }

        var isToBeAnnounced = eventLocation.IsToBeAnnounced;
        if (isToBeAnnounced
            && (request.RoomId.HasValue || facts.Room is not null))
        {
            return Hidden(request);
        }

        if (!isToBeAnnounced)
        {
            if (!HasValidPhysicalSource(request, eventLocation, facts.Location, facts.Room))
            {
                return Hidden(request);
            }

            var physicalLocation = facts.Location!;
            if (IsUnavailableLifecycleState(physicalLocation))
            {
                return HasValidUnavailableLifecycle(physicalLocation)
                    ? Unavailable(request)
                    : Hidden(request);
            }

            if (!HasValidActivePrivacyState(physicalLocation))
            {
                return Hidden(request);
            }
        }

        var location = facts.Location;
        var isPrivateHome = location?.LocationKindId == (int)LocationKindEnum.PrivateHome;
        if (isPrivateHome && !facts.Governance.AllowHomeLocations)
        {
            return Unavailable(request);
        }

        if (isToBeAnnounced)
        {
            return EventLocationDisclosureResult.Suppressed(
                request.EventLocationId,
                request.Purpose,
                EventLocationDisclosureState.ToBeAnnounced);
        }

        if (eventLocation.NeedsPrivacyReview)
        {
            return EventLocationDisclosureResult.Suppressed(
                request.EventLocationId,
                request.Purpose,
                EventLocationDisclosureState.NeedsPrivacyReview);
        }

        if (isPrivateHome && request.Purpose == EventLocationDisclosurePurpose.Public)
        {
            return EventLocationDisclosureResult.Public(
                request.EventLocationId,
                EventLocationDisclosureState.PrivateVenue,
                new EventLocationDisclosureValues(
                    VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel));
        }

        var exactAllowed = HasExactAuthority(facts, eventLocation, isPrivateHome);
        if (isPrivateHome
            && request.Purpose == EventLocationDisclosurePurpose.Attendee
            && !exactAllowed)
        {
            return EventLocationDisclosureResult.Attendee(
                request.EventLocationId,
                EventLocationDisclosureState.PrivateVenue,
                new EventLocationDisclosureValues(
                    VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel));
        }

        var values = SelectValues(facts, eventLocation, location!, exactAllowed);
        if (values.GetPresentFields().IsEmpty)
        {
            return Hidden(request);
        }

        return request.Purpose switch
        {
            EventLocationDisclosurePurpose.Public => EventLocationDisclosureResult.Public(
                request.EventLocationId,
                EventLocationDisclosureState.Available,
                values),
            EventLocationDisclosurePurpose.Attendee => EventLocationDisclosureResult.Attendee(
                request.EventLocationId,
                EventLocationDisclosureState.Available,
                values),
            EventLocationDisclosurePurpose.Management => EventLocationDisclosureResult.Management(
                request.EventLocationId,
                EventLocationDisclosureState.Available,
                values),
            _ => Hidden(request)
        };
    }

    private static bool HasValidRequest(EventLocationDisclosureRequest request)
        => request.TenantId != Guid.Empty
            && request.EventId != Guid.Empty
            && request.EventLocationId != Guid.Empty
            && (!request.RoomId.HasValue || request.RoomId.Value != Guid.Empty)
            && Enum.IsDefined(request.Purpose);

    private static bool HasValidAssociation(
        EventLocationDisclosureRequest request,
        EventLocation? eventLocation)
        => eventLocation is not null
            && !eventLocation.IsDeleted
            && eventLocation.Id == request.EventLocationId
            && eventLocation.TenantId == request.TenantId
            && eventLocation.EventId == request.EventId
            && eventLocation.PolicyVersion > 0
            && (!eventLocation.LocationId.HasValue || eventLocation.LocationId.Value != Guid.Empty)
            && eventLocation.HasValidLocationOrTbaShape;

    private static bool HasValidPhysicalSource(
        EventLocationDisclosureRequest request,
        EventLocation eventLocation,
        Location? location,
        LocationRoom? room)
    {
        if (eventLocation.LocationId is not { } locationId
            || location is null
            || location.Id != locationId
            || location.TenantId != request.TenantId)
        {
            return false;
        }

        if (request.RoomId is not { } roomId)
        {
            return room is null;
        }

        return room is not null
            && !room.IsDeleted
            && room.Id == roomId
            && room.TenantId == request.TenantId
            && room.LocationId == locationId;
    }

    private static bool IsUnavailableLifecycleState(Location location)
        => location.LocationPrivacyStateId is (int)LocationPrivacyStateEnum.NotProvided
            or (int)LocationPrivacyStateEnum.Erased;

    private static bool HasValidUnavailableLifecycle(Location location)
    {
        if (!Enum.IsDefined((LocationPrivacyStateEnum)location.LocationPrivacyStateId)
            || !Enum.IsDefined((LocationKindEnum)location.LocationKindId))
        {
            return false;
        }

        return (LocationPrivacyStateEnum)location.LocationPrivacyStateId switch
        {
            LocationPrivacyStateEnum.NotProvided =>
                location.Pii is null
                && location.PiiErasedAtUtc is null
                && location.PiiErasureReason is null
                && HasValidOwnerKind(location),
            LocationPrivacyStateEnum.Erased =>
                location.LocationKindId == (int)LocationKindEnum.PrivateHome
                && location.Pii is null
                && location.OwnerUserId is null
                && location.PiiErasedAtUtc is { } erasedAtUtc
                && erasedAtUtc != default
                && erasedAtUtc.Kind == DateTimeKind.Utc
                && location.PiiErasureReason is { } erasureReason
                && Enum.IsDefined(erasureReason),
            _ => false
        };
    }

    private static bool HasValidActivePrivacyState(Location location)
    {
        if (!Enum.IsDefined((LocationPrivacyStateEnum)location.LocationPrivacyStateId)
            || !Enum.IsDefined((LocationKindEnum)location.LocationKindId)
            || location.LocationPrivacyStateId != (int)LocationPrivacyStateEnum.Active
            || location.Pii is null
            || location.Pii.LocationId != location.Id
            || location.PiiErasedAtUtc.HasValue
            || location.PiiErasureReason.HasValue
            || !HasText(location.Pii.Address)
            || !HasText(location.Pii.Postcode))
        {
            return false;
        }

        return HasValidOwnerKind(location);
    }

    private static bool HasValidOwnerKind(Location location)
        => location.LocationKindId == (int)LocationKindEnum.PrivateHome
            ? location.OwnerUserId is { } ownerId && ownerId != Guid.Empty
            : location.OwnerUserId is null;

    private static bool HasValidGovernance(EventLocationDisclosureGovernanceFact governance)
        => governance.IsResolved
            && Enum.IsDefined(governance.MinimumHomeAudience)
            && governance.DefaultRevealOffset >= TimeSpan.Zero
            && governance.DefaultRevealOffset <= TimeSpan.FromDays(30);

    private static bool HasValidDisclosurePolicy(
        EventLocation eventLocation,
        TimeSpan defaultRevealOffset)
        => Enum.IsDefined((LocationDisclosureAudienceEnum)eventLocation.FullDetailsAudienceId)
            && TryResolveEffectiveRevealTime(
                eventLocation,
                defaultRevealOffset,
                out _);

    private static bool HasPurposeAuthority(
        EventLocationDisclosureRequest request,
        EventLocation eventLocation,
        EventLocationDisclosureEvaluationFacts facts)
    {
        var authority = facts.Authority;
        if (authority is null
            || !Enum.IsDefined(authority.Kind)
            || authority.Purpose != request.Purpose
            || authority.TenantId != request.TenantId
            || authority.EventId != request.EventId
            || authority.EventLocationId != request.EventLocationId)
        {
            return false;
        }

        return request.Purpose switch
        {
            EventLocationDisclosurePurpose.Public =>
                authority.Kind == EventLocationDisclosureAuthorityKind.Public
                && authority.RequesterUserId is null
                && authority.RegistrationAccess is null,
            EventLocationDisclosurePurpose.Attendee =>
                request.RequesterUserId is { } requesterId
                && requesterId != Guid.Empty
                && authority.Kind == EventLocationDisclosureAuthorityKind.AttendeeRegistration
                && authority.RequesterUserId == requesterId
                && HasMatchingRegistrationAccess(authority.RegistrationAccess, eventLocation),
            EventLocationDisclosurePurpose.Management =>
                request.RequesterUserId is { } requesterId
                && requesterId != Guid.Empty
                && authority.Kind == EventLocationDisclosureAuthorityKind.Management
                && authority.RequesterUserId == requesterId
                && authority.RegistrationAccess is null,
            _ => false
        };
    }

    private static bool HasValidServerTime(DateTimeOffset serverNowUtc)
        => serverNowUtc != default && serverNowUtc.Offset == TimeSpan.Zero;

    private static bool HasMatchingRegistrationAccess(
        EventLocationRegistrationAccess? access,
        EventLocation eventLocation)
        => access is not null
            && access.EventId == eventLocation.EventId
            && access.RequestedEventLocationId == eventLocation.Id
            && access.CoversRequestedEventLocation;

    private static bool HasExactAuthority(
        EventLocationDisclosureEvaluationFacts facts,
        EventLocation eventLocation,
        bool isPrivateHome)
    {
        var purpose = facts.Request.Purpose;
        if (purpose == EventLocationDisclosurePurpose.Management)
        {
            return true;
        }

        if (!TryResolveEffectiveRevealTime(
                eventLocation,
                facts.Governance.DefaultRevealOffset,
                out DateTime effectiveRevealFromUtc)
            || !RevealGateIsOpen(effectiveRevealFromUtc, facts.ServerNowUtc))
        {
            return false;
        }

        if (purpose == EventLocationDisclosurePurpose.Public)
        {
            return true;
        }

        var registrationAccess = facts.Authority?.RegistrationAccess;
        if (registrationAccess is null
            || !Enum.IsDefined((LocationDisclosureAudienceEnum)eventLocation.FullDetailsAudienceId))
        {
            return false;
        }

        var requiredAudience = (LocationDisclosureAudienceEnum)eventLocation.FullDetailsAudienceId;
        if (isPrivateHome)
        {
            requiredAudience = MoreRestrictive(
                requiredAudience,
                facts.Governance.MinimumHomeAudience);
        }

        return registrationAccess.AllowsAudience(requiredAudience);
    }

    private static bool TryResolveEffectiveRevealTime(
        EventLocation eventLocation,
        TimeSpan defaultRevealOffset,
        out DateTime effectiveRevealFromUtc)
    {
        effectiveRevealFromUtc = default;
        if (eventLocation.CreatedAt == default
            || eventLocation.CreatedAt.Kind != DateTimeKind.Utc
            || defaultRevealOffset < TimeSpan.Zero
            || defaultRevealOffset > TimeSpan.FromDays(30))
        {
            return false;
        }

        DateTime governedRevealFromUtc;
        try
        {
            governedRevealFromUtc = eventLocation.CreatedAt.Add(defaultRevealOffset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (eventLocation.RevealFullDetailsFromUtc is { } explicitRevealFromUtc)
        {
            if (explicitRevealFromUtc.Kind != DateTimeKind.Utc)
            {
                return false;
            }

            effectiveRevealFromUtc = explicitRevealFromUtc > governedRevealFromUtc
                ? explicitRevealFromUtc
                : governedRevealFromUtc;
            return true;
        }

        effectiveRevealFromUtc = governedRevealFromUtc;
        return true;
    }

    private static bool RevealGateIsOpen(DateTime revealFromUtc, DateTimeOffset serverNowUtc)
        => new DateTimeOffset(revealFromUtc) <= serverNowUtc;

    private static LocationDisclosureAudienceEnum MoreRestrictive(
        LocationDisclosureAudienceEnum first,
        LocationDisclosureAudienceEnum second)
        => RestrictionRank(first) >= RestrictionRank(second) ? first : second;

    private static int RestrictionRank(LocationDisclosureAudienceEnum audience)
        => audience switch
        {
            LocationDisclosureAudienceEnum.AnyCurrentRegistrant => 1,
            LocationDisclosureAudienceEnum.ConfirmedParticipant => 2,
            LocationDisclosureAudienceEnum.Never => 3,
            _ => int.MaxValue
        };

    private static EventLocationDisclosureValues SelectValues(
        EventLocationDisclosureEvaluationFacts facts,
        EventLocation eventLocation,
        Location location,
        bool exactAllowed)
    {
        var purpose = facts.Request.Purpose;
        var pii = location.Pii!;
        var room = facts.Room;
        var canShowAddress = exactAllowed
            && (purpose != EventLocationDisclosurePurpose.Public
                || facts.Governance.AllowPublicExactAddress);
        var canShowCoordinates = exactAllowed
            && (purpose != EventLocationDisclosurePurpose.Public
                || facts.Governance.AllowPublicCoordinates)
            && HasValidCoordinates(pii.Latitude, pii.Longitude);

        var country = Selected(eventLocation.ShowCountry, purpose, EventLocationDisclosureField.Country)
            ? TextOrNull(location.Country)
            : null;
        var city = Selected(eventLocation.ShowCity, purpose, EventLocationDisclosureField.City)
            ? TextOrNull(location.City)
            : null;
        var venueName = Selected(eventLocation.ShowVenueName, purpose, EventLocationDisclosureField.VenueName)
            ? TextOrNull(location.FullName)
            : null;
        var roomName = Selected(eventLocation.ShowRoomName, purpose, EventLocationDisclosureField.RoomName)
            ? TextOrNull(room?.Name)
            : null;
        var roomDescription = purpose == EventLocationDisclosurePurpose.Management
            && EventLocationDisclosureContract.IsWithinPurposeCeiling(
                purpose,
                EventLocationDisclosureField.RoomDescription)
            ? TextOrNull(room?.Description)
            : null;
        var streetAddress = canShowAddress
            && Selected(eventLocation.ShowStreetAddress, purpose, EventLocationDisclosureField.StreetAddress)
            ? TextOrNull(pii.Address)
            : null;
        var postcode = canShowAddress
            && Selected(eventLocation.ShowPostcode, purpose, EventLocationDisclosureField.Postcode)
            ? TextOrNull(pii.Postcode)
            : null;
        var latitude = canShowCoordinates
            && Selected(eventLocation.ShowCoordinates, purpose, EventLocationDisclosureField.Latitude)
            ? pii.Latitude
            : null;
        var longitude = canShowCoordinates
            && Selected(eventLocation.ShowCoordinates, purpose, EventLocationDisclosureField.Longitude)
            ? pii.Longitude
            : null;
        var formattedAddress = streetAddress is not null
            && WithinPurpose(purpose, EventLocationDisclosureField.FormattedAddress)
            ? TextOrNull(facts.Derivatives?.FormattedAddress)
            : null;
        var mapUrl = latitude.HasValue
            && longitude.HasValue
            && WithinPurpose(purpose, EventLocationDisclosureField.MapUrl)
            ? TextOrNull(facts.Derivatives?.MapUrl)
            : null;
        var geohash = latitude.HasValue
            && longitude.HasValue
            && WithinPurpose(purpose, EventLocationDisclosureField.Geohash)
            ? TextOrNull(facts.Derivatives?.Geohash)
            : null;

        return new EventLocationDisclosureValues(
            Country: country,
            City: city,
            VenueName: venueName,
            RoomName: roomName,
            RoomDescription: roomDescription,
            StreetAddress: streetAddress,
            Postcode: postcode,
            Latitude: latitude,
            Longitude: longitude,
            FormattedAddress: formattedAddress,
            MapUrl: mapUrl,
            Geohash: geohash);
    }

    private static bool Selected(
        bool selected,
        EventLocationDisclosurePurpose purpose,
        EventLocationDisclosureField field)
        => selected
            && WithinPurpose(purpose, field)
            && EventLocationDisclosureContract.HasCurrentlySatisfiablePolicyGate(field);

    private static bool WithinPurpose(
        EventLocationDisclosurePurpose purpose,
        EventLocationDisclosureField field)
        => EventLocationDisclosureContract.IsWithinPurposeCeiling(purpose, field)
            && EventLocationDisclosureContract.HasCurrentlySatisfiablePolicyGate(field);

    private static bool HasValidCoordinates(double? latitude, double? longitude)
        => latitude is { } lat
            && longitude is { } lon
            && double.IsFinite(lat)
            && double.IsFinite(lon)
            && lat is >= -90 and <= 90
            && lon is >= -180 and <= 180;

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string? TextOrNull(string? value) => HasText(value) ? value : null;

    private static EventLocationDisclosureResult Hidden(EventLocationDisclosureRequest request)
        => request.EventLocationId == Guid.Empty
            ? EventLocationDisclosureResult.HiddenForInvalidRequest(
                request.EventLocationId,
                request.Purpose)
            : EventLocationDisclosureResult.Suppressed(
                request.EventLocationId,
                request.Purpose,
                EventLocationDisclosureState.Hidden);

    private static EventLocationDisclosureResult Unavailable(EventLocationDisclosureRequest request)
        => EventLocationDisclosureResult.Suppressed(
            RequireResultId(request.EventLocationId),
            request.Purpose,
            EventLocationDisclosureState.Unavailable);

    private static Guid RequireResultId(Guid eventLocationId)
        => eventLocationId != Guid.Empty
            ? eventLocationId
            : throw new ArgumentException(
                "A non-empty EventLocation id is required to produce a disclosure result.",
                nameof(eventLocationId));
}
