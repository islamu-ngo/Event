using Explore.Application.DTOs.CustomPropertyDefinition;

namespace Explore.Application.DTOs.EventSessionCustomProperty;

public sealed record UpdateEventSessionCustomPropertyDefinitionDto
{
    public UpdateCustomPropertyDefinitionMetadataDto? Metadata { get; init; }
    public UpdateCustomPropertyDefinitionValidationDto? Validation { get; init; }
    public UpdateEventSessionCustomPropertyDefinitionOptionsDto? Options { get; init; }
}

public sealed record UpdateEventSessionCustomPropertyDefinitionOptionsDto
{
    private IReadOnlyList<CreateEventSessionCustomPropertyOptionDto>? _items;

    public IReadOnlyList<CreateEventSessionCustomPropertyOptionDto>? Items
    {
        get => _items;
        init => _items = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
