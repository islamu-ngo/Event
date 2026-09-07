namespace Explore.Application.Models.Storage;

public sealed record FileStorageDeleteResult(
    string Provider,
    string ObjectKey,
    bool Deleted);
