using Explore.Domain.Enums;

namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record EventCustomPropertyProjectionDto
{
    public Guid Id { get; init; }
    public Guid EventCustomPropertyDefinitionId { get; init; }
    public Guid EventCustomPropertyValueId { get; init; }
    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public PropertyType PropertyType { get; init; }
    public ExposureLevel ExposureLevel { get; init; }
    public bool IsSearchable { get; init; }
    public bool IsFilterable { get; init; }
    public bool IsExportable { get; init; }
    public bool IsModerationRelevant { get; init; }
    public bool IsAnalyticsRelevant { get; init; }
    public int Ordinal { get; init; }
    public Guid? OptionId { get; init; }
    public string? TextValue { get; init; }
    public decimal? NumberValue { get; init; }
    public bool? BooleanValue { get; init; }
    public DateTimeOffset? DateTimeValue { get; init; }
    public string? NormalizedValue { get; init; }
    public DateTime UpdatedAt { get; init; }
}
