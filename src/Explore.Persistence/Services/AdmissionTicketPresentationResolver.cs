using System.Collections.Immutable;
using Explore.Application.Contracts.Admissions;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class AdmissionTicketPresentationResolver(ExploreDbContext dbContext) :
    IAdmissionTicketPresentationResolver
{
    public async Task<ImmutableDictionary<Guid, AdmissionTicketPresentation>> ResolveAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> admissionTicketIds,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || admissionTicketIds.Count == 0)
        {
            return ImmutableDictionary<Guid, AdmissionTicketPresentation>.Empty;
        }

        Guid[] ticketIds = admissionTicketIds.Distinct().ToArray();
        TicketFact[] tickets = await (
                from ticket in dbContext.AdmissionTickets.AsNoTracking()
                join ticketType in dbContext.EventTicketTypes.AsNoTracking()
                    on new { ticket.TenantId, Id = ticket.EventTicketTypeId }
                    equals new { ticketType.TenantId, ticketType.Id }
                join pii in dbContext.RegistrationParticipantPii.AsNoTracking()
                    on new { ticket.TenantId, Id = ticket.ParticipantId }
                    equals new { pii.TenantId, Id = pii.RegistrationParticipantId }
                    into piiRows
                from pii in piiRows.DefaultIfEmpty()
                where ticket.TenantId == tenantId && ticketIds.Contains(ticket.Id)
                select new TicketFact(
                    ticket.Id,
                    pii == null ? null : pii.DisplayName,
                    ticketType.Name))
            .ToArrayAsync(cancellationToken);

        EntitlementFact[] entitlements = await (
                from ticket in dbContext.AdmissionTickets.AsNoTracking()
                join entitlement in dbContext.TicketTypeEntitlements.AsNoTracking()
                    on new { ticket.TenantId, TicketTypeId = ticket.EventTicketTypeId }
                    equals new { entitlement.TenantId, entitlement.TicketTypeId }
                join targetEvent in dbContext.Events.AsNoTracking()
                    on new { entitlement.TenantId, Id = entitlement.TargetEventId }
                    equals new { targetEvent.TenantId, targetEvent.Id }
                join day in dbContext.EventDays.AsNoTracking()
                    on new { entitlement.TenantId, Id = entitlement.EventDayId }
                    equals new { day.TenantId, Id = (Guid?)day.Id }
                    into dayRows
                from day in dayRows.DefaultIfEmpty()
                join session in dbContext.EventSessions.AsNoTracking()
                    on new { entitlement.TenantId, Id = entitlement.EventSessionId }
                    equals new { session.TenantId, Id = (Guid?)session.Id }
                    into sessionRows
                from session in sessionRows.DefaultIfEmpty()
                where ticket.TenantId == tenantId && ticketIds.Contains(ticket.Id)
                orderby ticket.Id,
                    entitlement.EntitlementScopeTypeId,
                    entitlement.EventDayId,
                    entitlement.EventSessionId,
                    entitlement.Id
                select new EntitlementFact(
                    ticket.Id,
                    entitlement.EntitlementScopeTypeId,
                    targetEvent.Title,
                    day == null ? null : day.Label,
                    day == null ? null : day.LocalDate,
                    session == null ? null : session.Title,
                    entitlement.IncludedQuantity))
            .ToArrayAsync(cancellationToken);

        ILookup<Guid, EntitlementFact> byTicket =
            entitlements.ToLookup(entitlement => entitlement.AdmissionTicketId);
        return tickets.ToImmutableDictionary(
            ticket => ticket.AdmissionTicketId,
            ticket => new AdmissionTicketPresentation(
                ticket.HolderDisplayName,
                ticket.TicketTypeName,
                byTicket[ticket.AdmissionTicketId]
                    .Select(entitlement => new AdmissionTicketEntitlementPresentation(
                        ScopeCode(entitlement.ScopeTypeId),
                        entitlement.EventTitle,
                        entitlement.DayLabel,
                        entitlement.LocalDate,
                        entitlement.SessionTitle,
                        entitlement.IncludedQuantity))
                    .ToImmutableArray()));
    }

    private static string ScopeCode(int scopeTypeId) =>
        (EntitlementScopeTypeEnum)scopeTypeId switch
        {
            EntitlementScopeTypeEnum.Event => "EVENT",
            EntitlementScopeTypeEnum.EventDay => "EVENT_DAY",
            EntitlementScopeTypeEnum.EventSession => "EVENT_SESSION",
            _ => "UNKNOWN"
        };

    private sealed record TicketFact(
        Guid AdmissionTicketId,
        string? HolderDisplayName,
        string TicketTypeName);

    private sealed record EntitlementFact(
        Guid AdmissionTicketId,
        int ScopeTypeId,
        string EventTitle,
        string? DayLabel,
        DateOnly? LocalDate,
        string? SessionTitle,
        int IncludedQuantity);
}
