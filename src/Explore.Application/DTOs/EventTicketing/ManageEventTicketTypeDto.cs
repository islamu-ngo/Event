namespace Explore.Application.DTOs.EventTicketing;

public sealed record ManageEventTicketTypeDto
{
    public string Name { get; init; } = string.Empty;
    public int TicketPricingModeId { get; init; }
    public long? FixedPriceMinor { get; init; }
    public long? MinimumPriceMinor { get; init; }
    public long? SuggestedPriceMinor { get; init; }
    public int ParticipantDataCollectionModeId { get; init; }
    public Guid? CapacityPoolId { get; init; }
    public int? MinimumAge { get; init; }
    public int? MaximumAge { get; init; }
    public bool RequiresGuardian { get; init; }
    public bool RequiresApproval { get; init; }
    public int? PerOrderLimit { get; init; }
    public int? PerAccountLimit { get; init; }
    public int? PerVerifiedContactLimit { get; init; }
    public int? PerBookingPartyLimit { get; init; }
    public IReadOnlyList<ManageTicketTypeEntitlementDto> Entitlements { get; init; } = [];
}
