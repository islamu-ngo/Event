namespace Explore.Blazor.Client.Services;

public interface IStorageObjectUrlResolver
{
    string? ResolvePublicImageUrl(string? storageReference);

    string? ResolvePublicImageUrl(Guid storageObjectId);

    string? ResolveContentUrl(Guid storageObjectId);
}

public sealed class StorageObjectUrlResolver : IStorageObjectUrlResolver
{
    private const string StorageObjectApiPrefix = "/api/storageobject/";

    public string? ResolvePublicImageUrl(string? storageReference)
    {
        if (string.IsNullOrWhiteSpace(storageReference))
        {
            return null;
        }

        var normalizedReference = NormalizeReference(storageReference);
        if (normalizedReference.StartsWith(StorageObjectApiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedReference;
        }

        return Guid.TryParse(normalizedReference, out var storageObjectId)
            ? ResolvePublicImageUrl(storageObjectId)
            : null;
    }

    public string? ResolvePublicImageUrl(Guid storageObjectId)
    {
        return storageObjectId == Guid.Empty
            ? null
            : $"{StorageObjectApiPrefix}{storageObjectId}/public";
    }

    public string? ResolveContentUrl(Guid storageObjectId)
    {
        return storageObjectId == Guid.Empty
            ? null
            : $"{StorageObjectApiPrefix}{storageObjectId}/content";
    }

    private static string NormalizeReference(string storageReference)
    {
        var trimmed = storageReference.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return uri.AbsolutePath;
        }

        return trimmed;
    }
}
