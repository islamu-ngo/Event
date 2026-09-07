namespace Explore.Application.DTOs.CustomPropertyDefinition;

public sealed record CreateCustomPropertyOptionDto
{
    public required string Namespace { get; init; }
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string Value { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}
