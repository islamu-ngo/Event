namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record AddedOptionDto(
    string Namespace,
    string Key,
    string DisplayName,
    string? Description,
    string Value,
    bool IsDefault,
    bool IsActive,
    int SortOrder,
    string? ParentOptionKey);
