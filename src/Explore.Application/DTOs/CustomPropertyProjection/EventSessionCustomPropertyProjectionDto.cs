using Explore.Domain.Enums;

namespace Explore.Application.DTOs.CustomPropertyProjection;

public sealed record EventSessionCustomPropertyProjectionDto
{
    public Guid Id { get; init; }
    public Guid EventSessionCustomPropertyDefinitionId { get; init; }
    public Guid EventSessionCustomPropertyValueId { get; init; }
    public Guid EventSessionId { get; init; }
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
