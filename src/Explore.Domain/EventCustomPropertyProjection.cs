using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventCustomPropertyProjection : ITenantEntity
{
    public Guid Id { get; set; }

    [ForeignKey(nameof(Definition))]
    public Guid EventCustomPropertyDefinitionId { get; set; }
    public EventCustomPropertyDefinition? Definition { get; set; }

    [ForeignKey(nameof(Value))]
    public Guid EventCustomPropertyValueId { get; set; }
    public EventCustomPropertyValue? Value { get; set; }

    [ForeignKey(nameof(Event))]
    public Guid EventId { get; set; }
    public Event? Event { get; set; }

    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public required string Namespace { get; set; }
    public required string Key { get; set; }
    public PropertyType PropertyType { get; set; }
    public ExposureLevel ExposureLevel { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsExportable { get; set; }
    public bool IsModerationRelevant { get; set; }
    public bool IsAnalyticsRelevant { get; set; }
    public int Ordinal { get; set; }

    [ForeignKey(nameof(Option))]
    public Guid? OptionId { get; set; }
    public EventCustomPropertyOption? Option { get; set; }

    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateTimeOffset? DateTimeValue { get; set; }
    public string? NormalizedValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}
