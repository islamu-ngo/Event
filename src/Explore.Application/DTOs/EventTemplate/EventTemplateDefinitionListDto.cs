using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventTemplate;

public sealed record EventTemplateDefinitionListDto
{
    public Guid Id { get; init; }
    public Guid EventTemplateId { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public PropertyType PropertyType { get; init; }
    public bool IsRequired { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public ExposureLevel ExposureLevel { get; init; }
    public int OptionCount { get; init; }
}
