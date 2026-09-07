using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventAddOnInventoryAllocation :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private EventAddOnInventoryAllocation()
    {
    }

    private EventAddOnInventoryAllocation(
        Guid id,
        Guid operationId,
        RegistrationOrderAddOnLine line,
        DateTime reservedAtUtc)
    {
        Id = id;
        OperationId = operationId;
        TenantId = line.TenantId;
        EventId = line.EventId;
        RegistrationOrderId = line.RegistrationOrderId;
        RegistrationOrderAddOnLineId = line.Id;
        EventAddOnCatalogItemId = line.EventAddOnCatalogItemId;
        Quantity = line.Quantity;
        ActiveUniquenessSlot = line.Id;
        ReservedAt = reservedAtUtc;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid OperationId { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventAddOnInventoryAllocation));
    }

    public Guid EventId { get; private set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid RegistrationOrderAddOnLineId { get; private set; }

    public Guid EventAddOnCatalogItemId { get; private set; }

    public int Quantity { get; private set; }

    public int ReleasedQuantity { get; private set; }

    public Guid? ActiveUniquenessSlot { get; private set; }

    public DateTime ReservedAt { get; private set; }

    public DateTime? ReleasedAt { get; private set; }

    public int AllocatedQuantity => checked(Quantity - ReleasedQuantity);

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static EventAddOnInventoryAllocation Create(
        Guid id,
        Guid operationId,
        RegistrationOrderAddOnLine line,
        DateTime reservedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (id == Guid.Empty || operationId == Guid.Empty)
        {
            throw new ArgumentException("Inventory allocation and operation identities are required.");
        }

        return new EventAddOnInventoryAllocation(
            id,
            operationId,
            line,
            RequireUtc(reservedAtUtc, nameof(reservedAtUtc)));
    }

    public void ReleaseQuantity(int quantity, DateTime releasedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        DateTime normalized = RequireUtc(releasedAtUtc, nameof(releasedAtUtc));
        if (normalized < ReservedAt ||
            ReleasedAt.HasValue && normalized < ReleasedAt.Value)
        {
            throw new ArgumentException(
                "Release timestamp cannot precede reservation or an earlier release.",
                nameof(releasedAtUtc));
        }
        if (quantity > AllocatedQuantity)
        {
            throw new InvalidOperationException("Released add-on quantity exceeds the active allocation.");
        }

        ReleasedQuantity = checked(ReleasedQuantity + quantity);
        ReleasedAt = normalized;
        if (ReleasedQuantity == Quantity)
        {
            ActiveUniquenessSlot = null;
        }

        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private static DateTime RequireUtc(DateTime value, string parameterName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("Timestamp must be UTC.", parameterName);
}
