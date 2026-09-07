using Explore.Application.DTOs.CustomPropertyDefinition;

namespace Explore.Application.DTOs.EventCustomProperty;

public sealed record UpdateEventCustomPropertyDefinitionDto
{
    public UpdateCustomPropertyDefinitionMetadataDto? Metadata { get; init; }
    public UpdateCustomPropertyDefinitionValidationDto? Validation { get; init; }
    public UpdateEventCustomPropertyDefinitionOptionsDto? Options { get; init; }
}

public sealed record UpdateEventCustomPropertyDefinitionOptionsDto
{
    private IReadOnlyList<CreateEventCustomPropertyOptionDto>? _items;

    public IReadOnlyList<CreateEventCustomPropertyOptionDto>? Items
    {
        get => _items;
        init => _items = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
