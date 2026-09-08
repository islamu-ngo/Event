namespace Explore.Application.Models.Storage;

public sealed record FileStorageWriteResult(
    string Provider,
    string ObjectKey,
    long SizeBytes,
    string ContentType,
    string? Sha256Checksum);
