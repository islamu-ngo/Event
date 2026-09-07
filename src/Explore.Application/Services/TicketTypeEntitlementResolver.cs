using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class TicketTypeEntitlementResolver(
    IEventDayRepository days,
    IEventSessionRepository sessions,
    ITenantContext tenant)
{
    public async Task<IReadOnlyList<TicketTypeEntitlement>> ResolveAsync(
        Guid ticketTypeId,
        IReadOnlyList<ManageTicketTypeEntitlementDto> values,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var resolved = new List<TicketTypeEntitlement>(values.Count);

        foreach (ManageTicketTypeEntitlementDto value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TicketTypeEntitlement entitlement = (EntitlementScopeTypeEnum)value.EntitlementScopeTypeId switch
            {
                EntitlementScopeTypeEnum.Event => TicketTypeEntitlement.CreateForEvent(
                    ticketTypeId,
                    tenant.TenantId,
                    eventId,
                    value.IncludedQuantity),
                EntitlementScopeTypeEnum.EventDay when value.EventDayId.HasValue =>
                    await ResolveDayAsync(ticketTypeId, value, eventId, cancellationToken),
                EntitlementScopeTypeEnum.EventSession when value.EventSessionId.HasValue =>
                    await ResolveSessionAsync(ticketTypeId, value, eventId, cancellationToken),
                _ => throw new ArgumentException("Entitlement scope is invalid.")
            };

            resolved.Add(entitlement);
        }

        return resolved;
    }

    private async Task<TicketTypeEntitlement> ResolveDayAsync(
        Guid ticketTypeId,
        ManageTicketTypeEntitlementDto value,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        EventDay? day = await days.GetByIdForEventAsync(
            value.EventDayId!.Value,
            eventId,
            tenant.TenantId,
            cancellationToken);

        if (day is null || day.EventId != eventId || day.TenantId != tenant.TenantId || day.IsDeleted)
        {
            throw new TicketingNotFoundException();
        }

        return TicketTypeEntitlement.CreateForEventDay(
            ticketTypeId,
            day,
            value.IncludedQuantity,
            (EntitlementSelectionRuleEnum)value.EntitlementSelectionRuleId);
    }

    private async Task<TicketTypeEntitlement> ResolveSessionAsync(
        Guid ticketTypeId,
        ManageTicketTypeEntitlementDto value,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        EventSession? session = await sessions.GetByIdForEventAsync(
            value.EventSessionId!.Value,
            eventId,
            tenant.TenantId,
            cancellationToken);

        if (session is null || session.EventId != eventId || session.TenantId != tenant.TenantId || session.IsDeleted)
        {
            throw new TicketingNotFoundException();
        }

        return TicketTypeEntitlement.CreateForEventSession(
            ticketTypeId,
            session,
            value.IncludedQuantity,
            (EntitlementSelectionRuleEnum)value.EntitlementSelectionRuleId);
    }
}
