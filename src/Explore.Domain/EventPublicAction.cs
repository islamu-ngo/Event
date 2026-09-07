using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class EventPublicAction : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid EventId { get; set; }
    public Event? Event { get; set; }
    public int EventPublicActionKindId { get; set; }
    public EventPublicActionKind? EventPublicActionKind { get; set; }
    public int HealthStateId { get; set; }
    public EventPublicActionHealthState? HealthState { get; set; }
    public string Url { get; private set; } = string.Empty;
    public string DestinationDomain { get; private set; } = string.Empty;
    public string? Label { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public void SetDestination(ExternalActionUrl destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Url = destination.Value;
        DestinationDomain = destination.DestinationDomain;
    }
}
