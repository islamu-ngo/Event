namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record UntouchedLocalDefinitionDto(
    string Namespace,
    string Key,
    string Reason);
