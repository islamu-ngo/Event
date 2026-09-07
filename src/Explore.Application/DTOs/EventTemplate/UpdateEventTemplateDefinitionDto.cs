namespace Explore.Application.DTOs.EventTemplate;

public sealed record UpdateEventTemplateDefinitionDto : CreateEventTemplateDefinitionDto
{
    public Guid Id { get; init; }
}
