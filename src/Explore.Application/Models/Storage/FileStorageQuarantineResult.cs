namespace Explore.Application.Models.Storage;

public sealed record FileStorageQuarantineResult(
    string Provider,
    string ObjectKey,
    FileStorageQuarantineOutcome Outcome)
{
    public bool Quarantined => Outcome is FileStorageQuarantineOutcome.Moved
        or FileStorageQuarantineOutcome.AlreadyQuarantined;
}

public enum FileStorageQuarantineOutcome
{
    Moved,
    AlreadyQuarantined,
    AlreadyAbsent,
    Blocked
}
