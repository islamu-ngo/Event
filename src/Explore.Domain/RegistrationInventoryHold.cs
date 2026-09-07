using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationInventoryHold : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private RegistrationInventoryHold()
    {
    }

    private RegistrationInventoryHold(
        Guid id,
        Guid registrationOrderId,
        Guid capacityPoolId,
        Guid ticketTypeId,
        Guid tenantId,
        int quantity,
        DateTime createdAt,
        DateTime expiresAt)
    {
        Id = id;
        RegistrationOrderId = registrationOrderId;
        CapacityPoolId = capacityPoolId;
        TicketTypeId = ticketTypeId;
        TenantId = tenantId;
        Quantity = quantity;
        RegistrationInventoryHoldStatusId = (int)RegistrationInventoryHoldStatusEnum.Active;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid CapacityPoolId { get; private set; }

    public Guid TicketTypeId { get; private set; }

    public Guid TenantId { get; set; }

    public int Quantity { get; private set; }

    public int RegistrationInventoryHoldStatusId { get; private set; }

    public RegistrationInventoryHoldStatus? RegistrationInventoryHoldStatus { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime? ConsumedAt { get; private set; }

    public DateTime? ReleasedAt { get; private set; }

    public bool IsCapacityAllocated => RegistrationInventoryHoldStatusId is
        (int)RegistrationInventoryHoldStatusEnum.Active or
        (int)RegistrationInventoryHoldStatusEnum.Consumed;

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public static RegistrationInventoryHold Create(
        Guid registrationOrderId,
        Guid capacityPoolId,
        Guid ticketTypeId,
        Guid tenantId,
        int quantity,
        DateTime createdAt,
        DateTime expiresAt) => Create(
        Guid.CreateVersion7(),
        registrationOrderId,
        capacityPoolId,
        ticketTypeId,
        tenantId,
        quantity,
        createdAt,
        expiresAt);

    public static RegistrationInventoryHold Create(
        Guid id,
        Guid registrationOrderId,
        Guid capacityPoolId,
        Guid ticketTypeId,
        Guid tenantId,
        int quantity,
        DateTime createdAt,
        DateTime expiresAt)
    {
        if (id == Guid.Empty || registrationOrderId == Guid.Empty || capacityPoolId == Guid.Empty || ticketTypeId == Guid.Empty || tenantId == Guid.Empty || quantity <= 0)
        {
            throw new ArgumentException("Order, pool, ticket type, tenant, and positive quantity are required.");
        }

        DateTime utcCreatedAt = EnsureUtc(createdAt, nameof(createdAt));
        DateTime utcExpiresAt = EnsureUtc(expiresAt, nameof(expiresAt));
        if (utcExpiresAt <= utcCreatedAt)
        {
            throw new ArgumentException("Hold expiry must be after creation.", nameof(expiresAt));
        }

        return new RegistrationInventoryHold(
            id,
            registrationOrderId,
            capacityPoolId,
            ticketTypeId,
            tenantId,
            quantity,
            utcCreatedAt,
            utcExpiresAt);
    }

    public bool TryConsume(DateTime consumedAt)
    {
        DateTime utcConsumedAt = EnsureUtc(consumedAt, nameof(consumedAt));
        if (RegistrationInventoryHoldStatusId != (int)RegistrationInventoryHoldStatusEnum.Active || utcConsumedAt >= ExpiresAt)
        {
            return false;
        }

        RegistrationInventoryHoldStatusId = (int)RegistrationInventoryHoldStatusEnum.Consumed;
        ConsumedAt = utcConsumedAt;
        UpdateConcurrency(utcConsumedAt);
        return true;
    }

    public bool TryRelease(DateTime releasedAt, RegistrationInventoryHoldStatusEnum outcome = RegistrationInventoryHoldStatusEnum.Released)
    {
        if (outcome is not (RegistrationInventoryHoldStatusEnum.Released or RegistrationInventoryHoldStatusEnum.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        DateTime utcReleasedAt = EnsureUtc(releasedAt, nameof(releasedAt));
        if (RegistrationInventoryHoldStatusId != (int)RegistrationInventoryHoldStatusEnum.Active)
        {
            return false;
        }

        RegistrationInventoryHoldStatusId = (int)outcome;
        ReleasedAt = utcReleasedAt;
        UpdateConcurrency(utcReleasedAt);
        return true;
    }

    public bool TryExpire(DateTime expiredAt)
    {
        DateTime utcExpiredAt = EnsureUtc(expiredAt, nameof(expiredAt));
        if (RegistrationInventoryHoldStatusId != (int)RegistrationInventoryHoldStatusEnum.Active || utcExpiredAt < ExpiresAt)
        {
            return false;
        }

        RegistrationInventoryHoldStatusId = (int)RegistrationInventoryHoldStatusEnum.Expired;
        ReleasedAt = utcExpiredAt;
        UpdateConcurrency(utcExpiredAt);
        return true;
    }

    public bool ExtendPaymentCutoff(DateTime cutoff, DateTime changedAt)
    {
        DateTime utcCutoff = EnsureUtc(cutoff, nameof(cutoff));
        DateTime utcChangedAt = EnsureUtc(changedAt, nameof(changedAt));
        if (RegistrationInventoryHoldStatusId != (int)RegistrationInventoryHoldStatusEnum.Active || utcCutoff <= ExpiresAt)
        {
            return false;
        }

        ExpiresAt = utcCutoff;
        UpdateConcurrency(utcChangedAt);
        return true;
    }

    private void UpdateConcurrency(DateTime timestamp)
    {
        UpdatedAt = timestamp;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
        }

        return value;
    }
}
