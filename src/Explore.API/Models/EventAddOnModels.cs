namespace Explore.API.Models;

public sealed record EventAddOnSelectionRequest
{
    public required Guid CatalogItemId { get; init; }
    public required int Quantity { get; init; }
}

public sealed record ReserveEventAddOnsRequest
{
    public required Guid CatalogId { get; init; }
    public IReadOnlyList<EventAddOnSelectionRequest> Selections { get; init; } = [];
}

public sealed record RefundEventAddOnRequest
{
    public required int Quantity { get; init; }
}

public sealed record ManageEventAddOnCatalogItemRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required long UnitPriceMinor { get; init; }
    public required int InventoryCapacity { get; init; }
    public required string FulfillmentDisclosure { get; init; }
    public required string RefundDisclosure { get; init; }
}

public sealed record CreateEventAddOnCatalogDraftRequest
{
    public required string CurrencyCode { get; init; }
}
