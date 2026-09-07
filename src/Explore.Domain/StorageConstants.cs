namespace Explore.Domain;

public static class StorageProviders
{
    public const string Local = "local";
    public const string S3Compatible = "s3_compatible";
    public const string LegacyExternal = "legacy_external";

    public static readonly string[] All = [Local, S3Compatible, LegacyExternal];
}

public static class StorageRouteKeys
{
    public const string Images = "images";
    public const string Documents = "documents";
    public const string General = "general";

    public static readonly string[] All = [Images, Documents, General];
}

public static class StorageObjectVisibilities
{
    public const string PublicImage = "public_image";
    public const string AuthenticatedTenant = "authenticated_tenant";
    public const string PrivateOwner = "private_owner";

    public static readonly string[] All = [PublicImage, AuthenticatedTenant, PrivateOwner];
}

public static class StorageObjectPurposes
{
    public const string LegacyImage = "legacy_image";
    public const string ProfileImage = "profile_image";
    public const string EventImage = "event_image";
    public const string Attachment = "attachment";
    public const string Document = "document";
    public const string SystemAsset = "system_asset";

    public static readonly string[] All = [LegacyImage, ProfileImage, EventImage, Attachment, Document, SystemAsset];
}

public static class StorageObjectLifecycleStates
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Quarantined = "quarantined";
    public const string DeleteRequested = "delete_requested";
    public const string Deleted = "deleted";

    public static readonly string[] All = [Pending, Active, Quarantined, DeleteRequested, Deleted];
}

public static class StorageUploadSessionStates
{
    public const string Reserved = "reserved";
    public const string Uploading = "uploading";
    public const string Finalized = "finalized";
    public const string Canceled = "canceled";
    public const string Failed = "failed";
    public const string Expired = "expired";

    public static readonly string[] All = [Reserved, Uploading, Finalized, Canceled, Failed, Expired];
}

public static class StorageOwningResourceKinds
{
    public const string OrganizationTenant = "organization_tenant";
}
