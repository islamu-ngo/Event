using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class EventAddOnRefundAllocation :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private EventAddOnRefundAllocation()
    {
    }

    private EventAddOnRefundAllocation(
        Guid id,
        Guid refundOperationId,
        RegistrationOrderAddOnLine line,
        int quantity,
        long amountMinor,
        DateTime allocatedAtUtc)
    {
        Id = id;
        RefundOperationId = refundOperationId;
        TenantId = line.TenantId;
        EventId = line.EventId;
        RegistrationOrderId = line.RegistrationOrderId;
        RegistrationOrderAddOnLineId = line.Id;
        Quantity = quantity;
        AmountMinor = amountMinor;
        CurrencyCode = line.CurrencyCodeSnapshot;
        AllocatedAt = allocatedAtUtc;
        Status = EventAddOnRefundAllocationStatus.PendingProvider;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid RefundOperationId { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventAddOnRefundAllocation));
    }

    public Guid EventId { get; private set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid RegistrationOrderAddOnLineId { get; private set; }

    public int Quantity { get; private set; }

    public long AmountMinor { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public DateTime AllocatedAt { get; private set; }

    public EventAddOnRefundAllocationStatus Status { get; private set; }

    public DateTime? ConfirmedAt { get; private set; }

    public DateTime? FailedAt { get; private set; }

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static EventAddOnRefundAllocation Create(
        Guid id,
        Guid refundOperationId,
        RegistrationOrderAddOnLine line,
        int quantity,
        DateTime allocatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (id == Guid.Empty || refundOperationId == Guid.Empty)
        {
            throw new ArgumentException("Refund allocation and operation identities are required.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (quantity > line.Quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Refund quantity cannot exceed the purchased add-on quantity.");
        }

        long amountMinor = MinorUnitMath.Multiply(line.UnitPriceMinorSnapshot, quantity);
        return new EventAddOnRefundAllocation(
            id,
            refundOperationId,
            line,
            quantity,
            amountMinor,
            allocatedAtUtc.Kind == DateTimeKind.Utc
                ? allocatedAtUtc
                : throw new ArgumentException("Timestamp must be UTC.", nameof(allocatedAtUtc)));
    }

    public bool TryConfirm(DateTime confirmedAtUtc)
    {
        DateTime normalized = RequireUtc(confirmedAtUtc, nameof(confirmedAtUtc));
        if (normalized < AllocatedAt)
        {
            throw new ArgumentException(
                "Confirmation cannot precede refund allocation.",
                nameof(confirmedAtUtc));
        }
        if (Status == EventAddOnRefundAllocationStatus.Confirmed)
        {
            return false;
        }

        if (Status != EventAddOnRefundAllocationStatus.PendingProvider)
        {
            throw new InvalidOperationException(
                "A failed add-on refund allocation cannot later be confirmed.");
        }

        Status = EventAddOnRefundAllocationStatus.Confirmed;
        ConfirmedAt = normalized;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public bool TryFail(DateTime failedAtUtc)
    {
        DateTime normalized = RequireUtc(failedAtUtc, nameof(failedAtUtc));
        if (normalized < AllocatedAt)
        {
            throw new ArgumentException(
                "Failure cannot precede refund allocation.",
                nameof(failedAtUtc));
        }
        if (Status == EventAddOnRefundAllocationStatus.Failed)
        {
            return false;
        }

        if (Status != EventAddOnRefundAllocationStatus.PendingProvider)
        {
            throw new InvalidOperationException(
                "A confirmed add-on refund allocation cannot later fail.");
        }

        Status = EventAddOnRefundAllocationStatus.Failed;
        FailedAt = normalized;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public bool TryConfirmInventoryReleasePending(DateTime confirmedAtUtc)
    {
        DateTime normalized = RequireUtc(
            confirmedAtUtc,
            nameof(confirmedAtUtc));
        if (normalized < AllocatedAt)
        {
            throw new ArgumentException(
                "Confirmation cannot precede refund allocation.",
                nameof(confirmedAtUtc));
        }

        if (Status ==
            EventAddOnRefundAllocationStatus.ConfirmedInventoryReleasePending)
        {
            return false;
        }

        if (Status != EventAddOnRefundAllocationStatus.PendingProvider)
        {
            throw new InvalidOperationException(
                "Only a pending add-on refund can enter inventory reconciliation.");
        }

        Status =
            EventAddOnRefundAllocationStatus.ConfirmedInventoryReleasePending;
        ConfirmedAt = normalized;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public bool TryCompleteInventoryRelease(DateTime releasedAtUtc)
    {
        DateTime normalized = RequireUtc(releasedAtUtc, nameof(releasedAtUtc));
        if (Status == EventAddOnRefundAllocationStatus.Confirmed)
        {
            return false;
        }

        if (Status !=
            EventAddOnRefundAllocationStatus.ConfirmedInventoryReleasePending ||
            ConfirmedAt.HasValue && normalized < ConfirmedAt.Value)
        {
            throw new InvalidOperationException(
                "Only confirmed pending inventory release can converge.");
        }

        Status = EventAddOnRefundAllocationStatus.Confirmed;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    private static DateTime RequireUtc(DateTime value, string parameterName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("Timestamp must be UTC.", parameterName);
}
