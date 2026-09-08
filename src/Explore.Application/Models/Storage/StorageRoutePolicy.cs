using Explore.Application.Contracts.Infrastructure;

namespace Explore.Application.Models.Storage;

public sealed record StoragePolicyIntent(
    string Purpose,
    string Visibility,
    string ContentType,
    string? OwningResourceKind = null,
    Guid? OwningResourceId = null,
    long? ExpectedSizeBytes = null);

public sealed record StorageRouteSetting(
    string RouteKey,
    string Provider,
    long MaxUploadBytes);

public sealed record StorageRouteMatrixDocument(
    int Version,
    IReadOnlyList<StorageRouteSetting> Routes)
{
    public static StorageRouteMatrixDocument Empty { get; } = new(1, []);
}

public sealed record ResolvedStorageRoutePolicy(
    string RouteKey,
    string Provider,
    long MaxUploadBytes,
    SettingSource ProviderSource,
    SettingSource MaxUploadSource);
