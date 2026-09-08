using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class TicketTypeEntitlement : ITenantEntity
{
    private TicketTypeEntitlement()
    {
    }

    private TicketTypeEntitlement(
        Guid ticketTypeId,
        Guid tenantId,
        Guid targetEventId,
        EntitlementScopeTypeEnum scopeType,
        Guid? eventDayId,
        Guid? eventSessionId,
        int includedQuantity,
        EntitlementSelectionRuleEnum selectionRule)
    {
        Id = Guid.CreateVersion7();
        TicketTypeId = ticketTypeId;
        TenantId = tenantId;
        TargetEventId = targetEventId;
        EntitlementScopeTypeId = (int)scopeType;
        EventDayId = eventDayId;
        EventSessionId = eventSessionId;
        ScopeId = scopeType switch
        {
            EntitlementScopeTypeEnum.Event => targetEventId,
            EntitlementScopeTypeEnum.EventDay => eventDayId!.Value,
            EntitlementScopeTypeEnum.EventSession => eventSessionId!.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(scopeType))
        };
        IncludedQuantity = includedQuantity;
        EntitlementSelectionRuleId = (int)selectionRule;
    }

    public Guid Id { get; private set; }

    public Guid TicketTypeId { get; private set; }

    public Guid TenantId { get; set; }

    public Guid TargetEventId { get; private set; }

    public int EntitlementScopeTypeId { get; private set; }

    public EntitlementScopeType? EntitlementScopeType { get; private set; }

    public Guid? EventDayId { get; private set; }

    public Guid? EventSessionId { get; private set; }

    public Guid ScopeId { get; private set; }

    public int IncludedQuantity { get; private set; }

    public int EntitlementSelectionRuleId { get; private set; }

    public EntitlementSelectionRule? EntitlementSelectionRule { get; private set; }

    public static TicketTypeEntitlement CreateForEvent(
        Guid ticketTypeId,
        Guid tenantId,
        Guid targetEventId,
        int includedQuantity)
    {
        return Create(ticketTypeId, tenantId, targetEventId, EntitlementScopeTypeEnum.Event, null, null, includedQuantity, EntitlementSelectionRuleEnum.AllIncluded);
    }

    public static TicketTypeEntitlement CreateForEventDay(
        Guid ticketTypeId,
        EventDay eventDay,
        int includedQuantity,
        EntitlementSelectionRuleEnum selectionRule)
    {
        ArgumentNullException.ThrowIfNull(eventDay);
        return Create(ticketTypeId, eventDay.TenantId, eventDay.EventId, EntitlementScopeTypeEnum.EventDay, eventDay.Id, null, includedQuantity, selectionRule);
    }

    public static TicketTypeEntitlement CreateForEventSession(
        Guid ticketTypeId,
        EventSession eventSession,
        int includedQuantity,
        EntitlementSelectionRuleEnum selectionRule)
    {
        ArgumentNullException.ThrowIfNull(eventSession);
        return Create(ticketTypeId, eventSession.TenantId, eventSession.EventId, EntitlementScopeTypeEnum.EventSession, null, eventSession.Id, includedQuantity, selectionRule);
    }

    internal TicketTypeEntitlement CloneTo(Guid ticketTypeId) => new(
        ticketTypeId,
        TenantId,
        TargetEventId,
        (EntitlementScopeTypeEnum)EntitlementScopeTypeId,
        EventDayId,
        EventSessionId,
        IncludedQuantity,
        (EntitlementSelectionRuleEnum)EntitlementSelectionRuleId);

    private static TicketTypeEntitlement Create(
        Guid ticketTypeId,
        Guid tenantId,
        Guid targetEventId,
        EntitlementScopeTypeEnum scopeType,
        Guid? eventDayId,
        Guid? eventSessionId,
        int includedQuantity,
        EntitlementSelectionRuleEnum selectionRule)
    {
        if (ticketTypeId == Guid.Empty || tenantId == Guid.Empty || targetEventId == Guid.Empty)
        {
            throw new ArgumentException("Ticket type, tenant, and target event are required.");
        }

        TicketCatalogRules.ValidateEntitlementShape(scopeType, eventDayId, eventSessionId, includedQuantity, selectionRule);
        return new TicketTypeEntitlement(ticketTypeId, tenantId, targetEventId, scopeType, eventDayId, eventSessionId, includedQuantity, selectionRule);
    }
}
