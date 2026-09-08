namespace Explore.Application.DTOs.EventTemplateSync;

public sealed record EventTemplateSyncHistoryItemDto(
    Guid EventId,
    int BaseProvenanceVersion,
    int TargetTemplateVersion,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<SyncConflictDto> Conflicts,
    Guid? ActorId,
    DateTimeOffset SyncedAt);
