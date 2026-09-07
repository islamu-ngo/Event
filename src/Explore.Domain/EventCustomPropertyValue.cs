using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventCustomPropertyValue : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    [ForeignKey(nameof(Definition))]
    public Guid EventCustomPropertyDefinitionId { get; set; }
    public EventCustomPropertyDefinition? Definition { get; set; }

    [ForeignKey(nameof(Event))]
    public Guid EventId { get; set; }
    public Event? Event { get; set; }

    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public int Ordinal { get; set; }
    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateTimeOffset? DateTimeValue { get; set; }

    [ForeignKey(nameof(Option))]
    public Guid? OptionId { get; set; }
    public EventCustomPropertyOption? Option { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
