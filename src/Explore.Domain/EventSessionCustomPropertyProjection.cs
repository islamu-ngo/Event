using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventSessionCustomPropertyProjection : ITenantEntity
{
    public Guid Id { get; set; }

    [ForeignKey(nameof(Definition))]
    public Guid EventSessionCustomPropertyDefinitionId { get; set; }
    public EventSessionCustomPropertyDefinition? Definition { get; set; }

    [ForeignKey(nameof(Value))]
    public Guid EventSessionCustomPropertyValueId { get; set; }
    public EventSessionCustomPropertyValue? Value { get; set; }

    [ForeignKey(nameof(EventSession))]
    public Guid EventSessionId { get; set; }
    public EventSession? EventSession { get; set; }

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
    public EventSessionCustomPropertyOption? Option { get; set; }

    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateTimeOffset? DateTimeValue { get; set; }
    public string? NormalizedValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}
