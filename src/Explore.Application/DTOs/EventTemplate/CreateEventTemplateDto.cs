namespace Explore.Application.DTOs.EventTemplate;

public sealed record CreateEventTemplateDto
{
    private IReadOnlyList<CreateEventTemplateDefinitionDto>? _definitions = Array.AsReadOnly(Array.Empty<CreateEventTemplateDefinitionDto>());

    public string TemplateKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? EventTypeId { get; set; }
    public int Version { get; set; } = 1;
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public IReadOnlyList<CreateEventTemplateDefinitionDto> Definitions
    {
        get => _definitions!;
        init => _definitions = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
