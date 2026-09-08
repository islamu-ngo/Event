namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record RetiredDefinitionDto(
    string Namespace,
    string Key,
    Guid CurrentConcurrencyStamp);
