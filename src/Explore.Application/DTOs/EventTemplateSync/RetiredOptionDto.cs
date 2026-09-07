namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record RetiredOptionDto(
    string Namespace,
    string Key,
    Guid CurrentConcurrencyStamp);
