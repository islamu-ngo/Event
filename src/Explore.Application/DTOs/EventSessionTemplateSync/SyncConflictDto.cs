namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record SyncConflictDto(
    string Key,
    string Reason);
