using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class PromotionReservation : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private PromotionReservation()
    {
    }

    private PromotionReservation(Guid id, Guid tenantId, Guid registrationOrderId, Guid promotionDefinitionVersionId, Guid promotionCodeId, DateTime reservedAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        RegistrationOrderId = registrationOrderId;
        PromotionDefinitionVersionId = promotionDefinitionVersionId;
        PromotionCodeId = promotionCodeId;
        PromotionReservationStatusId = (int)PromotionReservationStatusEnum.Active;
        OrderReservationSlot = Guid.Empty;
        ReservedAtUtc = reservedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid PromotionDefinitionVersionId { get; private set; }

    public Guid PromotionCodeId { get; private set; }

    public int PromotionReservationStatusId { get; private set; }

    public Guid OrderReservationSlot { get; private set; }

    public DateTime ReservedAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? ReleasedAtUtc { get; private set; }

    public DateTime? ExpiredAtUtc { get; private set; }

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static PromotionReservation Reserve(RegistrationOrder order, PromotionDefinition definition, PromotionCode code, DateTime reservedAtUtc) =>
        Reserve(Guid.CreateVersion7(), order, definition, code, reservedAtUtc);

    public static PromotionReservation Reserve(Guid id, RegistrationOrder order, PromotionDefinition definition, PromotionCode code, DateTime reservedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(code);
        DateTime normalizedReservedAt = EnsureUtc(reservedAtUtc, nameof(reservedAtUtc));

        if (id == Guid.Empty || definition.TenantId != order.TenantId || code.TenantId != order.TenantId ||
            code.PromotionDefinitionVersionId != definition.Id ||
            definition.ScopeMetadata != code.ScopeMetadata ||
            definition.ScopeMetadata.EventId != order.EventId ||
            definition.ScopeMetadata.TicketCatalogVersionId != order.TicketCatalogVersionId ||
            !string.Equals(definition.ScopeMetadata.CurrencyCode, order.CurrencyCode, StringComparison.Ordinal))
        {
            throw new ArgumentException("Promotion reservation must match the order and published code context.");
        }

        return new PromotionReservation(id, order.TenantId, order.Id, definition.Id, code.Id, normalizedReservedAt);
    }

    public bool TryConsume(DateTime consumedAtUtc)
    {
        DateTime normalizedConsumedAt = EnsureUtc(consumedAtUtc, nameof(consumedAtUtc));
        if (PromotionReservationStatusId == (int)PromotionReservationStatusEnum.Consumed)
        {
            return false;
        }

        if (PromotionReservationStatusId != (int)PromotionReservationStatusEnum.Active)
        {
            throw new InvalidOperationException("Only active promotion reservations can be consumed.");
        }

        PromotionReservationStatusId = (int)PromotionReservationStatusEnum.Consumed;
        OrderReservationSlot = Id;
        ConsumedAtUtc = normalizedConsumedAt;
        UpdateConcurrency(normalizedConsumedAt);
        return true;
    }

    public bool TryRelease(DateTime releasedAtUtc)
    {
        DateTime normalizedReleasedAt = EnsureUtc(releasedAtUtc, nameof(releasedAtUtc));
        if (PromotionReservationStatusId == (int)PromotionReservationStatusEnum.Released)
        {
            return false;
        }

        if (PromotionReservationStatusId != (int)PromotionReservationStatusEnum.Active)
        {
            throw new InvalidOperationException("Only active promotion reservations can be released.");
        }

        PromotionReservationStatusId = (int)PromotionReservationStatusEnum.Released;
        OrderReservationSlot = Id;
        ReleasedAtUtc = normalizedReleasedAt;
        UpdateConcurrency(normalizedReleasedAt);
        return true;
    }

    public bool TryExpire(DateTime expiredAtUtc)
    {
        DateTime normalizedExpiredAt = EnsureUtc(expiredAtUtc, nameof(expiredAtUtc));
        if (PromotionReservationStatusId == (int)PromotionReservationStatusEnum.Expired)
        {
            return false;
        }

        if (PromotionReservationStatusId != (int)PromotionReservationStatusEnum.Active)
        {
            throw new InvalidOperationException("Only active promotion reservations can expire.");
        }

        PromotionReservationStatusId = (int)PromotionReservationStatusEnum.Expired;
        OrderReservationSlot = Id;
        ExpiredAtUtc = normalizedExpiredAt;
        UpdateConcurrency(normalizedExpiredAt);
        return true;
    }

    private void UpdateConcurrency(DateTime timestamp)
    {
        UpdatedAt = timestamp;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }

        return value;
    }
}
