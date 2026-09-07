using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.EventSessionTemplate;

public sealed record UpdateEventSessionTemplateDto
{
    public UpdateEventSessionTemplateMetadataDto? Metadata { get; init; }
    public UpdateEventSessionTemplateDefinitionsDto? Definitions { get; init; }
}

public sealed record UpdateEventSessionTemplateMetadataDto
{
    public string? SessionTemplateKey { get; init; }
    public string? DisplayName { get; init; }
    public OptionalUpdate<string> Description { get; init; }
    public int? Version { get; init; }
    public bool? IsPublished { get; init; }
    public bool? IsActive { get; init; }
    public int? SortOrder { get; init; }
}

public sealed record UpdateEventSessionTemplateDefinitionsDto
{
    private IReadOnlyList<CreateEventSessionTemplateDefinitionDto>? _items;

    public IReadOnlyList<CreateEventSessionTemplateDefinitionDto>? Items
    {
        get => _items;
        init => _items = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
