using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public static class RegistrationAdmissionMaterializer
{
    public static IReadOnlyList<(TicketTypeEntitlement Entitlement, EventSession Session)> Expand(
        EventTicketType ticketType,
        IReadOnlyCollection<EventSession> sessions) => ticketType.Entitlements
        .OrderBy(entitlement => entitlement.Id)
        .SelectMany(entitlement => ResolveSessions(entitlement, sessions)
            .OrderBy(session => session.Id)
            .Select(session => (entitlement, session)))
        .ToArray();

    public static EventRegistration Create(
        Guid admissionId,
        Guid concurrencyStamp,
        RegistrationOrder order,
        RegistrationOrderLine line,
        TicketTypeEntitlement entitlement,
        EventSession session,
        RegistrationParticipant participant,
        int ordinal,
        DateTime createdAt) => new()
        {
            Id = admissionId,
            ConcurrencyStamp = concurrencyStamp,
            EventId = order.EventId,
            Event = null!,
            LinkedUserId = participant.LinkedUserId,
            LinkedUser = null,
            EventSessionId = session.Id,
            EventSession = null!,
            RegistrationOrderId = order.Id,
            RegistrationOrderLineId = line.Id,
            TicketTypeEntitlementId = entitlement.Id,
            RegistrationParticipantId = participant.Id,
            RegistrationParticipant = participant,
            EntitlementOrdinal = ordinal,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            TenantId = order.TenantId,
            Tenant = null!,
            CoverageEstablishedAt = createdAt
        };

    private static IEnumerable<EventSession> ResolveSessions(
        TicketTypeEntitlement entitlement,
        IEnumerable<EventSession> sessions)
    {
        if ((EntitlementSelectionRuleEnum)entitlement.EntitlementSelectionRuleId is EntitlementSelectionRuleEnum.ChooseOne or EntitlementSelectionRuleEnum.ChooseUpToN)
        {
            throw new InvalidOperationException("Registration order requires a session selection before admission materialization.");
        }

        return (EntitlementScopeTypeEnum)entitlement.EntitlementScopeTypeId switch
        {
            EntitlementScopeTypeEnum.Event => sessions.Where(session => session.EventId == entitlement.TargetEventId),
            EntitlementScopeTypeEnum.EventDay => sessions.Where(session => session.EventDayId == entitlement.EventDayId),
            EntitlementScopeTypeEnum.EventSession => sessions.Where(session => session.Id == entitlement.EventSessionId),
            _ => throw new InvalidOperationException("Registration order entitlement scope is invalid.")
        };
    }
}
