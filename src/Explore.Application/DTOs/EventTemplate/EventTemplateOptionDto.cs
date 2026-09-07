namespace Explore.Application.DTOs.EventTemplate;

public sealed record EventTemplateOptionDto
{
    public Guid Id { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Value { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
}
