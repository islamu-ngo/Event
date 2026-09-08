namespace Explore.Application.DTOs.EventSessionTemplateSync;

public sealed record TemplateSyncOutcomeDto(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<SyncConflictDto> Conflicts,
    int NewProvenanceVersion,
    DateTimeOffset SyncedAt);
