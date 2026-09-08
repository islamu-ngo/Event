namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record SyncConflictDto(
    string Key,
    string Reason);
