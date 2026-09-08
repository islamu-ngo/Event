namespace Explore.Application.Models.Storage;

public sealed record StorageObjectContentResult(
    Stream Content,
    string ContentType,
    long Length,
    DateTimeOffset? LastModified,
    string? Sha256Checksum,
    string SafeDisplayName = "download",
    bool ShouldDownloadAsAttachment = true);
