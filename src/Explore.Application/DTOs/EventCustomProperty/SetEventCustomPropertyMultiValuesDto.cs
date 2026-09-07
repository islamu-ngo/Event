namespace Explore.Application.DTOs.EventCustomProperty;

public sealed record SetEventCustomPropertyMultiValuesDto
{
    private IReadOnlyList<SetEventCustomPropertyValueDto>? _values = Array.AsReadOnly(Array.Empty<SetEventCustomPropertyValueDto>());

    public Guid DefinitionId { get; init; }
    public Guid EventId { get; init; }
    public IReadOnlyList<SetEventCustomPropertyValueDto> Values
    {
        get => _values!;
        init => _values = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
