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

        var normalizedReference = storageReference.Trim();
        if (normalizedReference.StartsWith(StorageObjectApiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var segments = normalizedReference[StorageObjectApiPrefix.Length..].Split('/');
            return segments.Length == 2
                && Guid.TryParse(segments[0], out var id)
                && segments[1].Equals("public", StringComparison.OrdinalIgnoreCase)
                    ? ResolvePublicImageUrl(id)
                    : null;
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

}
