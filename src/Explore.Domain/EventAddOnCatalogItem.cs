using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class EventAddOnCatalogItem :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxDisclosureLength = 2000;

    private Guid _tenantId;

    private EventAddOnCatalogItem()
    {
    }

    private EventAddOnCatalogItem(
        Guid id,
        Guid tenantId,
        Guid catalogVersionId,
        string name,
        string? description,
        Money unitPrice,
        int inventoryCapacity,
        string fulfillmentDisclosure,
        string refundDisclosure)
    {
        Id = id;
        TenantId = tenantId;
        EventAddOnCatalogVersionId = catalogVersionId;
        Name = name;
        Description = description;
        UnitPriceMinor = unitPrice.MinorUnits;
        CurrencyCode = unitPrice.CurrencyCode;
        InventoryCapacity = inventoryCapacity;
        FulfillmentDisclosure = fulfillmentDisclosure;
        RefundDisclosure = refundDisclosure;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventAddOnCatalogItem));
    }

    public Guid EventAddOnCatalogVersionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public long UnitPriceMinor { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public int InventoryCapacity { get; private set; }

    public string FulfillmentDisclosure { get; private set; } = string.Empty;

    public string RefundDisclosure { get; private set; } = string.Empty;

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static EventAddOnCatalogItem Create(
        Guid id,
        Guid tenantId,
        Guid eventAddOnCatalogVersionId,
        string name,
        string? description,
        Money unitPrice,
        int inventoryCapacity,
        string fulfillmentDisclosure,
        string refundDisclosure)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);
        if (id == Guid.Empty || tenantId == Guid.Empty || eventAddOnCatalogVersionId == Guid.Empty)
        {
            throw new ArgumentException("Add-on item identity and tenant lineage are required.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryCapacity);

        return new EventAddOnCatalogItem(
            id,
            tenantId,
            eventAddOnCatalogVersionId,
            NormalizeRequired(name, MaxNameLength, nameof(name)),
            NormalizeOptional(description, MaxDescriptionLength, nameof(description)),
            unitPrice,
            inventoryCapacity,
            NormalizeRequired(
                fulfillmentDisclosure,
                MaxDisclosureLength,
                nameof(fulfillmentDisclosure)),
            NormalizeRequired(refundDisclosure, MaxDisclosureLength, nameof(refundDisclosure)));
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        string normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"Value must be at most {maximumLength} characters.",
                parameterName);
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"Value must be at most {maximumLength} characters.",
                parameterName);
    }
}
