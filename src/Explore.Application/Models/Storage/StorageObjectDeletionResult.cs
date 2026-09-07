namespace Explore.Application.Models.Storage;

public sealed record StorageObjectDeletionResult(
    int ScannedCount,
    int DeletedCount,
    int MissingKeyDeletedCount,
    int FailedCount)
{
    public bool CompletedWithoutFailures => FailedCount == 0;
}
