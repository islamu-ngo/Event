using Explore.Domain;

namespace Explore.API.Models;

public sealed record ReserveTicketPurchaseRequest
{
    public TicketPurchaseAccessMode AccessMode { get; init; }
    public Guid? RequestedPurchaserActorId { get; init; }
}

public sealed record TicketPurchaseGovernanceResource
{
    public required Guid OrderId { get; init; }
    public required TicketPurchaseAccessMode AccessMode { get; init; }
    public required bool SupportsHardCrossOrderCeiling { get; init; }
    public required string EnforcementScopeCode { get; init; }
}
