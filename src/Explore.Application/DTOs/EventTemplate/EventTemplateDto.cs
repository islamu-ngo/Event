namespace Explore.Application.DTOs.EventTemplate;

public sealed record EventTemplateDto
{
    private IReadOnlyList<EventTemplateDefinitionDto>? _definitions = Array.AsReadOnly(Array.Empty<EventTemplateDefinitionDto>());

    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid TenantId { get; init; }
    public string TemplateKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? EventTypeId { get; init; }
    public int Version { get; init; }
    public bool IsPublished { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public IReadOnlyList<EventTemplateDefinitionDto> Definitions
    {
        get => _definitions!;
        init => _definitions = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
