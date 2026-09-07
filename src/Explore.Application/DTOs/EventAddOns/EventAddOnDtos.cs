using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventAddOns;

public sealed record EventAddOnCatalogDto
{
    public Guid? Id { get; init; }
    public int? VersionNumber { get; init; }
    public string? CurrencyCode { get; init; }
    public IReadOnlyList<EventAddOnCatalogItemDto> Items { get; init; } = [];

    [JsonIgnore]
    public Guid EventId { get; init; }
    [JsonIgnore]
    public bool CanManage { get; init; }
    [JsonIgnore]
    public bool CanCreateDraft { get; init; }
    [JsonIgnore]
    public bool CanAddItem { get; init; }
    [JsonIgnore]
    public bool CanPublish { get; init; }
    [JsonIgnore]
    public bool CanRetire { get; init; }
    [JsonIgnore]
    public bool IsManagementView { get; init; }
}

public sealed record EventAddOnCatalogItemDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required long UnitPriceMinor { get; init; }
    public required string CurrencyCode { get; init; }
    public required bool IsAvailable { get; init; }
    public required int MaximumSelectableQuantity { get; init; }
    public required string FulfillmentDisclosure { get; init; }
    public required string RefundDisclosure { get; init; }
}

public sealed record RegistrationOrderAddOnSummaryDto
{
    public required Guid RegistrationOrderId { get; init; }
    public required string CurrencyCode { get; init; }
    public required long AddOnTotalMinor { get; init; }
    public required long GrandTotalMinor { get; init; }
    public IReadOnlyList<RegistrationOrderAddOnLineDto> Lines { get; init; } = [];

    [JsonIgnore]
    public Guid EventId { get; init; }
    [JsonIgnore]
    public bool CanReserve { get; init; }
}

public sealed record RegistrationOrderAddOnLineDto
{
    public required Guid Id { get; init; }
    public required Guid CatalogItemId { get; init; }
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public required long UnitPriceMinor { get; init; }
    public required long LineTotalMinor { get; init; }
    public required string CurrencyCode { get; init; }
    public required string FulfillmentDisclosure { get; init; }
    public required string RefundDisclosure { get; init; }
    public required string FulfillmentStatusCode { get; init; }
    public required int RefundAllocatedQuantity { get; init; }
    public required long RefundAllocatedMinor { get; init; }
    public required string RefundStatusCode { get; init; }
    public required int MaximumRefundableQuantity { get; init; }

    [JsonIgnore]
    public Guid EventId { get; init; }
    [JsonIgnore]
    public Guid RegistrationOrderId { get; init; }
    [JsonIgnore]
    public bool CanFulfill { get; init; }
    [JsonIgnore]
    public bool CanRefund { get; init; }
}
