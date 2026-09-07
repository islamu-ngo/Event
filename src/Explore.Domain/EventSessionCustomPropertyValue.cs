using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventSessionCustomPropertyValue : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    [ForeignKey(nameof(Definition))]
    public Guid EventSessionCustomPropertyDefinitionId { get; set; }
    public EventSessionCustomPropertyDefinition? Definition { get; set; }

    [ForeignKey(nameof(EventSession))]
    public Guid EventSessionId { get; set; }
    public EventSession? EventSession { get; set; }

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
    public EventSessionCustomPropertyOption? Option { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
