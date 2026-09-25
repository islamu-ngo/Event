namespace Explore.Application.Models.Storage;

/// <summary>Delete acceptance is not absence proof; a delete marker does not remove historical bytes.</summary>
public sealed record FileStorageDeleteResult(
    string Provider,
    string ObjectKey,
    bool Deleted,
    string? ProviderVersionId = null,
    bool DeleteMarkerCreated = false);
