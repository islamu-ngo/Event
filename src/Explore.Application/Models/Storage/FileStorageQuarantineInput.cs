namespace Explore.Application.Models.Storage;

public sealed record FileStorageQuarantineInput(
    string ObjectKey,
    string Reason,
    DateTime UtcNow);
