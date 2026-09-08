namespace Explore.Application.DTOs.EventTemplate;

public sealed record EventTemplateListDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string TemplateKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? EventTypeId { get; init; }
    public int Version { get; init; }
    public bool IsPublished { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public int DefinitionCount { get; init; }
}
