namespace Explore.Application.Models.Storage;

public sealed record StorageReconciliationResult(
    DateTime UtcNow,
    bool DryRun,
    int ScannedMetadataCount,
    int MissingBackingObjectCount,
    int QuarantinedMetadataCount,
    int DeleteEligibleMetadataCount,
    int DeletedMetadataCount,
    int ScannedBackingObjectCount,
    int OrphanBackingObjectCount,
    int QuarantinedBackingObjectCount,
    int SkippedCount,
    int FailedCount);
