using Explore.Application.Models.Common;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.CustomPropertyDefinition;

public sealed record UpdateCustomPropertyDefinitionDto
{
    public UpdateCustomPropertyDefinitionRelationsDto? Relations { get; init; }
    public UpdateCustomPropertyDefinitionMetadataDto? Metadata { get; init; }
    public UpdateCustomPropertyDefinitionValidationDto? Validation { get; init; }
    public UpdateCustomPropertyDefinitionOptionsDto? Options { get; init; }
}

public sealed record UpdateCustomPropertyDefinitionRelationsDto
{
    public EntityTypeName? EntityTypeName { get; init; }
}

public sealed record UpdateCustomPropertyDefinitionMetadataDto
{
    public string? Namespace { get; init; }
    public string? Key { get; init; }
    public string? DisplayName { get; init; }
    public OptionalUpdate<string?> Description { get; init; } = OptionalUpdate<string?>.Unspecified();
    public bool? IsActive { get; init; }
    public int? SortOrder { get; init; }
    public ExposureLevel? ExposureLevel { get; init; }
    public bool? IsSearchable { get; init; }
    public bool? IsFilterable { get; init; }
    public bool? IsExportable { get; init; }
    public bool? IsModerationRelevant { get; init; }
    public bool? IsAnalyticsRelevant { get; init; }
    public bool? IsSystemOwned { get; init; }
}

public sealed record UpdateCustomPropertyDefinitionValidationDto
{
    public PropertyType? PropertyType { get; init; }
    public bool? IsRequired { get; init; }
    public bool? IsMulti { get; init; }
    public OptionalUpdate<string?> DefaultTextValue { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<decimal?> DefaultNumberValue { get; init; } = OptionalUpdate<decimal?>.Unspecified();
    public OptionalUpdate<bool?> DefaultBooleanValue { get; init; } = OptionalUpdate<bool?>.Unspecified();
    public OptionalUpdate<DateTimeOffset?> DefaultDateTimeValue { get; init; } = OptionalUpdate<DateTimeOffset?>.Unspecified();
    public OptionalUpdate<int?> MinLength { get; init; } = OptionalUpdate<int?>.Unspecified();
    public OptionalUpdate<int?> MaxLength { get; init; } = OptionalUpdate<int?>.Unspecified();
    public OptionalUpdate<string?> RegexPattern { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<decimal?> MinNumber { get; init; } = OptionalUpdate<decimal?>.Unspecified();
    public OptionalUpdate<decimal?> MaxNumber { get; init; } = OptionalUpdate<decimal?>.Unspecified();
    public OptionalUpdate<DateTimeOffset?> MinDateTime { get; init; } = OptionalUpdate<DateTimeOffset?>.Unspecified();
    public OptionalUpdate<DateTimeOffset?> MaxDateTime { get; init; } = OptionalUpdate<DateTimeOffset?>.Unspecified();
    public OptionalUpdate<string?> AllowedUrlSchemes { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateCustomPropertyDefinitionOptionsDto
{
    private IReadOnlyList<CreateCustomPropertyOptionDto>? _items;

    public IReadOnlyList<CreateCustomPropertyOptionDto>? Items
    {
        get => _items;
        init => _items = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
