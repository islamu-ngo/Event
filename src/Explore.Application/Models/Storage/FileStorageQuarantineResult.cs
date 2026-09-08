namespace Explore.Application.Models.Storage;

public sealed record FileStorageQuarantineResult(
    string Provider,
    string ObjectKey,
    bool Quarantined);
