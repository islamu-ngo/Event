using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventSessionCustomProperty;

public sealed record EventSessionCustomPropertyDefinitionDto
{
    private IReadOnlyList<EventSessionCustomPropertyOptionDto>? _options = Array.AsReadOnly(Array.Empty<EventSessionCustomPropertyOptionDto>());

    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid EventSessionId { get; init; }
    public Guid TenantId { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
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

    public string? DefaultTextValue { get; init; }
    public decimal? DefaultNumberValue { get; init; }
    public bool? DefaultBooleanValue { get; init; }
    public DateTimeOffset? DefaultDateTimeValue { get; init; }
    public Guid? DefaultOptionId { get; init; }

    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public string? RegexPattern { get; init; }
    public decimal? MinNumber { get; init; }
    public decimal? MaxNumber { get; init; }
    public DateTimeOffset? MinDateTime { get; init; }
    public DateTimeOffset? MaxDateTime { get; init; }
    public string? AllowedUrlSchemes { get; init; }

    public Guid? SourceTemplateId { get; init; }
    public string? SourceTemplateKey { get; init; }
    public int? SourceTemplateVersion { get; init; }
    public Guid? SourceTemplateDefinitionId { get; init; }
    public DateTimeOffset InstantiatedAt { get; init; }
    public DateTimeOffset? LastSyncedFromTemplateAt { get; init; }

    public IReadOnlyList<EventSessionCustomPropertyOptionDto> Options
    {
        get => _options!;
        init => _options = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
