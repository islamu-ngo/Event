namespace Explore.Application.DTOs.EventSessionTemplate;

public sealed record UpdateEventSessionTemplateDefinitionDto : CreateEventSessionTemplateDefinitionDto
{
    public Guid Id { get; init; }
}
