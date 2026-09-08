namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record RetiredOptionDto(
    string Namespace,
    string Key,
    Guid CurrentConcurrencyStamp);
