namespace Explore.Application.DTOs.EventTicketing;

public sealed record TicketTypeEntitlementDto
{
    public int EntitlementScopeTypeId { get; init; }
    public string? EntitlementScopeTypeCode { get; init; }
    public string? EntitlementScopeTypeName { get; init; }
    public Guid? EventDayId { get; init; }
    public Guid? EventSessionId { get; init; }
    public int IncludedQuantity { get; init; }
    public int EntitlementSelectionRuleId { get; init; }
    public string? EntitlementSelectionRuleCode { get; init; }
    public string? EntitlementSelectionRuleName { get; init; }
}
