namespace Explore.Application.Models.Storage;

/// <summary>The original retained claim and file observation; execution must not replace or extend them.</summary>
public sealed record FileStorageQuarantineInput(
    string ObjectKey,
    Guid QuarantineId,
    long ExpectedSize,
    DateTime ExpectedLastModifiedUtc,
    Guid ProviderBindingId,
    Guid ClaimStamp,
    DateTime LeaseExpiresAtUtc,
    string CanonicalPhysicalPath,
    string PhysicalPathHash);
