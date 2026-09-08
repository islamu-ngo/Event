namespace Explore.Application.DTOs.EventSessionTemplate;

public sealed record EventSessionTemplateListDto
{
    public Guid Id { get; init; }
    public Guid EventTemplateId { get; init; }
    public Guid TenantId { get; init; }
    public string SessionTemplateKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Version { get; init; }
    public bool IsPublished { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public int DefinitionCount { get; init; }
}
