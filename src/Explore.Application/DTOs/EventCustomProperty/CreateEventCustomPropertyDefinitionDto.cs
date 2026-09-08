using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventCustomProperty;

public sealed record CreateEventCustomPropertyDefinitionDto
{
    private IReadOnlyList<CreateEventCustomPropertyOptionDto>? _options = Array.AsReadOnly(Array.Empty<CreateEventCustomPropertyOptionDto>());

    public Guid EventId { get; init; }
    public string Namespace { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PropertyType PropertyType { get; set; }
    public bool IsRequired { get; set; }
    public bool IsMulti { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public ExposureLevel ExposureLevel { get; set; }

    public bool IsSearchable { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsExportable { get; set; }
    public bool IsModerationRelevant { get; set; }
    public bool IsAnalyticsRelevant { get; set; }
    public bool IsSystemOwned { get; set; }

    public string? DefaultTextValue { get; set; }
    public decimal? DefaultNumberValue { get; set; }
    public bool? DefaultBooleanValue { get; set; }
    public DateTimeOffset? DefaultDateTimeValue { get; set; }
    public Guid? DefaultOptionId { get; init; }

    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string? RegexPattern { get; set; }
    public decimal? MinNumber { get; set; }
    public decimal? MaxNumber { get; set; }
    public DateTimeOffset? MinDateTime { get; set; }
    public DateTimeOffset? MaxDateTime { get; set; }
    public string? AllowedUrlSchemes { get; set; }

    public IReadOnlyList<CreateEventCustomPropertyOptionDto> Options
    {
        get => _options!;
        init => _options = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
