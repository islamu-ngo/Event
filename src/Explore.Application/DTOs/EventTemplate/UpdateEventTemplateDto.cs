using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.EventTemplate;

public sealed record UpdateEventTemplateDto
{
    public UpdateEventTemplateMetadataDto? Metadata { get; init; }
    public UpdateEventTemplateDefinitionsDto? Definitions { get; init; }
}

public sealed record UpdateEventTemplateMetadataDto
{
    public string? TemplateKey { get; init; }
    public string? DisplayName { get; init; }
    public OptionalUpdate<string> Description { get; init; }
    public OptionalUpdate<int> EventTypeId { get; init; }
    public int? Version { get; init; }
    public bool? IsPublished { get; init; }
    public bool? IsActive { get; init; }
    public int? SortOrder { get; init; }
}

public sealed record UpdateEventTemplateDefinitionsDto
{
    private IReadOnlyList<CreateEventTemplateDefinitionDto>? _items;

    public IReadOnlyList<CreateEventTemplateDefinitionDto>? Items
    {
        get => _items;
        init => _items = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
