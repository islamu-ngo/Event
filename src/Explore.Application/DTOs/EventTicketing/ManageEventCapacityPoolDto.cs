namespace Explore.Application.DTOs.EventTicketing;

public sealed record ManageEventCapacityPoolDto
{
    public string Name { get; init; } = string.Empty;
    public int? MaximumQuantity { get; init; }
    public int HoldDurationSeconds { get; init; }
    public int CapacityHoldPolicyId { get; init; }
    public int CapacityOversellPolicyId { get; init; }
    public bool IsActive { get; init; }
}
