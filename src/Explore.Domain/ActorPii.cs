// Uses a 1:1 shared primary-key relationship with Actor for hard-deleteable PII.

namespace Explore.Domain;

public class ActorPii
{
    public Guid ActorId { get; set; }
    public Actor? Actor { get; set; }

    public required string DisplayName { get; set; }
    private Guid? _profilePictureStorageObjectId;
    private string? _externalProfilePictureUri;

    public Guid? ProfilePictureStorageObjectId
    {
        get => _profilePictureStorageObjectId;
        set => SetProfilePicture(value, _externalProfilePictureUri);
    }

    public string? ExternalProfilePictureUri
    {
        get => _externalProfilePictureUri;
        set => SetProfilePicture(_profilePictureStorageObjectId, value);
    }

    public StorageObject? ProfilePicture { get; set; }

    public void SetProfilePicture(Guid? storageObjectId, string? externalUri)
    {
        if (!IsValidProfilePicture(storageObjectId, externalUri))
            throw new ArgumentException("A profile image must be absent, a managed storage ID, or an external HTTP(S) URI.");

        if (_profilePictureStorageObjectId != storageObjectId)
            ProfilePicture = null;
        _profilePictureStorageObjectId = storageObjectId;
        _externalProfilePictureUri = externalUri;
    }

    public static bool IsValidProfilePicture(Guid? storageObjectId, string? externalUri) =>
        storageObjectId != Guid.Empty
        && (externalUri is null
            || storageObjectId is null
            && externalUri.Length is > 0 and <= 500
            && externalUri == externalUri.Trim()
            && Uri.TryCreate(externalUri, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo));
}
