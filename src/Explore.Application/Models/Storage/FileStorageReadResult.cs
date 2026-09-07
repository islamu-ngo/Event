namespace Explore.Application.Models.Storage;

public sealed record FileStorageReadResult(
    Stream Content,
    string ContentType,
    long Length,
    DateTimeOffset? LastModified);
