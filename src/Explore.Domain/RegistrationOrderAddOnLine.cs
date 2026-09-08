using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class RegistrationOrderAddOnLine :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private RegistrationOrderAddOnLine()
    {
    }

    private RegistrationOrderAddOnLine(
        Guid id,
        RegistrationOrder order,
        EventAddOnCatalogVersion catalog,
        EventAddOnCatalogItem item,
        int quantity,
        long lineTotalMinor)
    {
        Id = id;
        RegistrationOrderId = order.Id;
        TenantId = order.TenantId;
        EventId = order.EventId;
        EventAddOnCatalogVersionId = catalog.Id;
        EventAddOnCatalogItemId = item.Id;
        Quantity = quantity;
        NameSnapshot = item.Name;
        UnitPriceMinorSnapshot = item.UnitPriceMinor;
        LineTotalMinorSnapshot = lineTotalMinor;
        CurrencyCodeSnapshot = item.CurrencyCode;
        FulfillmentDisclosureSnapshot = item.FulfillmentDisclosure;
        RefundDisclosureSnapshot = item.RefundDisclosure;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(RegistrationOrderAddOnLine));
    }

    public Guid EventId { get; private set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid EventAddOnCatalogVersionId { get; private set; }

    public Guid EventAddOnCatalogItemId { get; private set; }

    public int Quantity { get; private set; }

    public string NameSnapshot { get; private set; } = string.Empty;

    public long UnitPriceMinorSnapshot { get; private set; }

    public long LineTotalMinorSnapshot { get; private set; }

    public string CurrencyCodeSnapshot { get; private set; } = string.Empty;

    public string FulfillmentDisclosureSnapshot { get; private set; } = string.Empty;

    public string RefundDisclosureSnapshot { get; private set; } = string.Empty;

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static RegistrationOrderAddOnLine Create(
        Guid id,
        RegistrationOrder order,
        EventAddOnCatalogVersion catalog,
        EventAddOnCatalogItem item,
        int quantity)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(item);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Add-on order line identity is required.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (!catalog.IsPublished ||
            catalog.TenantId != order.TenantId ||
            catalog.EventId != order.EventId ||
            item.TenantId != order.TenantId ||
            item.EventAddOnCatalogVersionId != catalog.Id ||
            !catalog.Items.Contains(item) ||
            !string.Equals(catalog.CurrencyCode, order.CurrencyCode, StringComparison.Ordinal) ||
            !string.Equals(item.CurrencyCode, order.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Add-on lines require an item from the event's pinned published catalog and order currency.");
        }

        long lineTotalMinor = MinorUnitMath.Multiply(item.UnitPriceMinor, quantity);
        return new RegistrationOrderAddOnLine(
            id,
            order,
            catalog,
            item,
            quantity,
            lineTotalMinor);
    }
}
