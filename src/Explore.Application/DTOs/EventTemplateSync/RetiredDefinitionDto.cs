namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record RetiredDefinitionDto(
    string Namespace,
    string Key,
    Guid CurrentConcurrencyStamp);
