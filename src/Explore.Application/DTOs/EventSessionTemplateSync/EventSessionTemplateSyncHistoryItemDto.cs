namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record EventSessionTemplateSyncHistoryItemDto(
    Guid EventSessionId,
    int BaseProvenanceVersion,
    int TargetTemplateVersion,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<SyncConflictDto> Conflicts,
    Guid? ActorId,
    DateTimeOffset SyncedAt);
