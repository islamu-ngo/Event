namespace Explore.Application.Models.Storage;

public sealed record FileStorageInventoryObject(
    string Provider,
    string ObjectKey,
    long SizeBytes,
    DateTime? LastModifiedUtc);
