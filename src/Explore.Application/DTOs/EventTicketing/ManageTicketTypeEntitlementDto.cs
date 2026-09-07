namespace Explore.Application.DTOs.EventTicketing;

public sealed record ManageTicketTypeEntitlementDto
{
    public int EntitlementScopeTypeId { get; init; }
    public Guid? EventDayId { get; init; }
    public Guid? EventSessionId { get; init; }
    public int IncludedQuantity { get; init; }
    public int EntitlementSelectionRuleId { get; init; }
}
