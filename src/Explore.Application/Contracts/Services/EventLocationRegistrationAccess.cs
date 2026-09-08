using System.Collections.Immutable;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

public enum EventLocationRegistrationEffectiveState
{
    Denied = 0,
    Pending = 1,
    Waitlisted = 2,
    Confirmed = 3,
    Rejected = 4,
    Cancelled = 5,
    Revoked = 6,
    NonLive = 7
}

public sealed class EventLocationRegistrationAccess
{
    internal EventLocationRegistrationAccess(
        Guid orderId,
        EventLocationRegistrationEffectiveState effectiveState,
        Guid eventId,
        bool coversWholeEvent,
        Guid? coveredEventDayId,
        ImmutableArray<Guid> coveredEventSessionIds,
        Guid requestedEventLocationId,
        bool coversRequestedEventLocation)
    {
        OrderId = orderId;
        EffectiveState = Enum.IsDefined(effectiveState)
            ? effectiveState
            : EventLocationRegistrationEffectiveState.Denied;
        EventId = eventId;
        RequestedEventLocationId = requestedEventLocationId;
        CoversRequestedEventLocation = coversRequestedEventLocation && HasAudienceAuthority(EffectiveState);
        CoversWholeEvent = CoversRequestedEventLocation && coversWholeEvent;
        CoveredEventDayId = CoversRequestedEventLocation ? coveredEventDayId : null;
        CoveredEventSessionIds = CoversRequestedEventLocation && !coveredEventSessionIds.IsDefault
            ? coveredEventSessionIds.Distinct().Order().ToImmutableArray()
            : [];
        AudienceCeiling = CoversRequestedEventLocation
            ? ResolveAudienceCeiling(EffectiveState)
            : LocationDisclosureAudienceEnum.Never;
    }

    public Guid OrderId { get; }
    public EventLocationRegistrationEffectiveState EffectiveState { get; }
    public Guid EventId { get; }
    public bool CoversWholeEvent { get; }
    public Guid? CoveredEventDayId { get; }
    public ImmutableArray<Guid> CoveredEventSessionIds { get; }
    public Guid RequestedEventLocationId { get; }
    public bool CoversRequestedEventLocation { get; }
    public LocationDisclosureAudienceEnum AudienceCeiling { get; }

    public bool AllowsAudience(LocationDisclosureAudienceEnum audience)
        => CoversRequestedEventLocation && (EffectiveState, AudienceCeiling, audience) switch
        {
            (EventLocationRegistrationEffectiveState.Pending or EventLocationRegistrationEffectiveState.Waitlisted,
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant) => true,
            (EventLocationRegistrationEffectiveState.Confirmed,
                LocationDisclosureAudienceEnum.ConfirmedParticipant,
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant or LocationDisclosureAudienceEnum.ConfirmedParticipant) => true,
            _ => false
        };

    private static bool HasAudienceAuthority(EventLocationRegistrationEffectiveState state)
        => state is EventLocationRegistrationEffectiveState.Pending
            or EventLocationRegistrationEffectiveState.Waitlisted
            or EventLocationRegistrationEffectiveState.Confirmed;

    private static LocationDisclosureAudienceEnum ResolveAudienceCeiling(EventLocationRegistrationEffectiveState state)
        => state switch
        {
            EventLocationRegistrationEffectiveState.Pending or EventLocationRegistrationEffectiveState.Waitlisted =>
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
            EventLocationRegistrationEffectiveState.Confirmed => LocationDisclosureAudienceEnum.ConfirmedParticipant,
            _ => LocationDisclosureAudienceEnum.Never
        };
}
