namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record UntouchedLocalDefinitionDto(
    string Namespace,
    string Key,
    string Reason);
