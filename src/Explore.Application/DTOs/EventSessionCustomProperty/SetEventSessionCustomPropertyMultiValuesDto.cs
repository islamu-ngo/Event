namespace Explore.Application.DTOs.EventSessionCustomProperty;

public sealed record SetEventSessionCustomPropertyMultiValuesDto
{
    private IReadOnlyList<SetEventSessionCustomPropertyValueDto>? _values = Array.AsReadOnly(Array.Empty<SetEventSessionCustomPropertyValueDto>());

    public Guid DefinitionId { get; init; }
    public Guid EventSessionId { get; init; }
    public IReadOnlyList<SetEventSessionCustomPropertyValueDto> Values
    {
        get => _values!;
        init => _values = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
