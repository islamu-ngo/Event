namespace Explore.Application.DTOs.EventSessionTemplate;

public sealed record EventSessionTemplateDto
{
    private IReadOnlyList<EventSessionTemplateDefinitionDto>? _definitions = Array.AsReadOnly(Array.Empty<EventSessionTemplateDefinitionDto>());

    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid EventTemplateId { get; init; }
    public Guid TenantId { get; init; }
    public string SessionTemplateKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Version { get; init; }
    public bool IsPublished { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public IReadOnlyList<EventSessionTemplateDefinitionDto> Definitions
    {
        get => _definitions!;
        init => _definitions = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
