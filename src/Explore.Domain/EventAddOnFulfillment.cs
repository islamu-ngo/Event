using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventAddOnFulfillment :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private EventAddOnFulfillment()
    {
    }

    private EventAddOnFulfillment(
        Guid id,
        Guid operationId,
        RegistrationOrderAddOnLine line,
        DateTime fulfilledAtUtc)
    {
        Id = id;
        OperationId = operationId;
        TenantId = line.TenantId;
        EventId = line.EventId;
        RegistrationOrderId = line.RegistrationOrderId;
        RegistrationOrderAddOnLineId = line.Id;
        FulfilledAt = fulfilledAtUtc;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid OperationId { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventAddOnFulfillment));
    }

    public Guid EventId { get; private set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid RegistrationOrderAddOnLineId { get; private set; }

    public DateTime FulfilledAt { get; private set; }

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static EventAddOnFulfillment Create(
        Guid id,
        Guid operationId,
        RegistrationOrderAddOnLine line,
        DateTime fulfilledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (id == Guid.Empty || operationId == Guid.Empty)
        {
            throw new ArgumentException("Fulfillment and operation identities are required.");
        }

        return new EventAddOnFulfillment(
            id,
            operationId,
            line,
            fulfilledAtUtc.Kind == DateTimeKind.Utc
                ? fulfilledAtUtc
                : throw new ArgumentException("Timestamp must be UTC.", nameof(fulfilledAtUtc)));
    }
}
