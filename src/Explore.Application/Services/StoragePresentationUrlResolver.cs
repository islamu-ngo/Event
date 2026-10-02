using Microsoft.Extensions.Logging;
using Explore.Domain;

namespace Explore.Application.Services;

public static class StoragePresentationUrlResolver
{
    private const string StorageObjectApiPathPrefix = "/api/storageobject/";

    public static bool HasManagedBytes(StorageObject? storageObject) =>
        storageObject is { Id: var id, StorageProviderBindingId: { } bindingId }
        && id != Guid.Empty && bindingId != Guid.Empty
        && storageObject.Provider is StorageProviders.Local or StorageProviders.S3Compatible
        && !string.IsNullOrWhiteSpace(storageObject.ObjectKey);

    public static string? PublicImageUri(StorageObject? image) =>
        HasManagedBytes(image)
        && SafeRasterContentPolicy.IsSafePublicImageMetadata(image)
        && image!.Purpose != StorageObjectPurposes.EventResource
        && image.OwningResourceKind != StorageOwningResourceKinds.EventResource
            ? $"{StorageObjectApiPathPrefix}{image.Id}/public"
            : null;

    public static string? PublicImageUri(StorageObject? image, Guid tenantId) =>
        tenantId != Guid.Empty && image?.TenantId == tenantId ? PublicImageUri(image) : null;

    public static string? DeliveryUri(StorageObject storageObject) =>
        !HasManagedBytes(storageObject) || storageObject.IsDeleted
        || storageObject.LifecycleState != StorageObjectLifecycleStates.Active
        || storageObject.Purpose == StorageObjectPurposes.EventResource
        || storageObject.OwningResourceKind == StorageOwningResourceKinds.EventResource
            ? null
            : storageObject.Visibility == StorageObjectVisibilities.PublicImage
                ? PublicImageUri(storageObject)
                : storageObject.Visibility is StorageObjectVisibilities.AuthenticatedTenant or StorageObjectVisibilities.PrivateOwner
                    ? $"{StorageObjectApiPathPrefix}{storageObject.Id}/content"
                    : null;

    public static bool IsManagedProfileImage(StorageObject? image) =>
        PublicImageUri(image) is not null
        && image!.OwningResourceKind is null
        && image.OwningResourceId is null;

    public static Guid? ManagedProfilePictureId(ActorPii? pii) =>
        pii is { ExternalProfilePictureUri: null, ProfilePictureStorageObjectId: { } id }
        && pii.ProfilePicture?.Id == id
        && IsManagedProfileImage(pii.ProfilePicture)
            ? id
            : null;

    public static string? ExternalProfilePictureUri(ActorPii? pii) =>
        pii is { ProfilePictureStorageObjectId: null }
        && ActorPii.IsValidProfilePicture(null, pii.ExternalProfilePictureUri)
            ? pii.ExternalProfilePictureUri
            : null;

    public static string? ActorProfilePictureUri(ActorPii? pii) =>
        ManagedProfilePictureId(pii) is { } id
            ? $"{StorageObjectApiPathPrefix}{id}/public"
            : ExternalProfilePictureUri(pii);

    public static string? PublicProfileImageUri(StorageObject? image, Guid tenantId) =>
        IsManagedProfileImage(image) && image!.TenantId == tenantId && tenantId != Guid.Empty
            ? $"{StorageObjectApiPathPrefix}{image.Id}/public"
            : null;

    public static Task<string?> ResolveImageUrlAsync(
        string? objectKeyOrUri,
        ILogger logger,
        string imageContext)
    {
        var candidate = objectKeyOrUri?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Task.FromResult<string?>(null);
        }

        if (candidate.StartsWith('/'))
        {
            return Task.FromResult(ToPublicStorageObjectApiPath(candidate));
        }

        if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>(candidate);
        }

        logger.LogWarning("Rejected raw storage image reference for {ImageContext}.", imageContext);
        return Task.FromResult<string?>(null);
    }

    private static string? ToPublicStorageObjectApiPath(string path)
    {
        if (!path.StartsWith(StorageObjectApiPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = path[StorageObjectApiPathPrefix.Length..].Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 2
            && Guid.TryParse(segments[0], out var storageObjectId)
            && storageObjectId != Guid.Empty
            && segments[1].Equals("public", StringComparison.OrdinalIgnoreCase)
                ? $"{StorageObjectApiPathPrefix}{storageObjectId}/public"
                : null;
    }
}
