namespace Explore.Domain.Enums;

public enum EventResourceAudienceKindEnum
{
    Public = 1,
    AuthenticatedTenantMember = 2,
    SessionRegistrant = 3,
    TicketHolder = 4,
    CheckedInParticipant = 5,
    AnyEventSessionSpeaker = 6,
    SessionSpeaker = 7,
    EventStaff = 8,
    Organizer = 9
}
