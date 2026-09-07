using Explore.Domain.Enums;

namespace Explore.Application.DTOs.CustomPropertyDefinition;

public sealed record CustomPropertyDefinitionListDto
{
    public Guid Id { get; init; }
    public EntityTypeName EntityTypeName { get; init; }
    public required string Namespace { get; init; }
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public PropertyType PropertyType { get; init; }
    public bool IsRequired { get; init; }
    public bool IsMulti { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public ExposureLevel ExposureLevel { get; init; }
    public bool IsSearchable { get; init; }
    public bool IsFilterable { get; init; }
    public bool IsExportable { get; init; }
    public bool IsModerationRelevant { get; init; }
    public bool IsAnalyticsRelevant { get; init; }
    public bool IsSystemOwned { get; init; }
    public int OptionCount { get; init; }
}
