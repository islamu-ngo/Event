using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Domain.Settings.Definitions;

public static class StorageSettingDefinitions
{
    public static readonly SettingDefinition Provider = new(
        Key: GovernanceSettingKeys.Storage.Provider,
        ValueType: SettingValueType.String,
        DefaultValue: $"\"{StorageProviders.Local}\"",
        Category: "ObjectStorage",
        Description: "Selected storage provider. Local filesystem is the default; S3-compatible storage is optional.",
        MaxScope: SettingScope.Tenant,
        AllowedValues: StorageProviders.All);

    public static readonly SettingDefinition DefaultMaxUploadBytes = new(
        Key: GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
        ValueType: SettingValueType.Long,
        DefaultValue: "10485760",
        Category: "ObjectStorage",
        Description: "Default maximum upload size in bytes for tenant storage policy.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition DefaultTenantQuotaBytes = new(
        Key: GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
        ValueType: SettingValueType.Long,
        DefaultValue: "1073741824",
        Category: "ObjectStorage",
        Description: "Default tenant storage quota in bytes.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition InstanceMaxUploadBytes = new(
        Key: GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
        ValueType: SettingValueType.Long,
        DefaultValue: "104857600",
        Category: "ObjectStorage",
        Description: "Instance-wide upload ceiling in bytes; tenant overrides cannot exceed this value.",
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition RouteMatrix = new(
        Key: GovernanceSettingKeys.Storage.RouteMatrix,
        ValueType: SettingValueType.Json,
        DefaultValue: "{\"version\":1,\"routes\":[]}",
        Category: "ObjectStorage",
        Description: "Versioned route matrix for server-side provider selection by upload purpose and content type.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition Endpoint = new(
        Key: "s3.endpoint",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "ObjectStorage",
        Description: "S3-compatible endpoint URL (e.g., https://fsn1.your-objectstorage.com)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition PublicEndpoint = new(
        Key: "s3.public_endpoint",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "ObjectStorage",
        Description: "Public endpoint for presigned URLs (if different from internal endpoint)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition BucketName = new(
        Key: "s3.bucket_name",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "ObjectStorage",
        Description: "S3 bucket name for object storage",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition Region = new(
        Key: "s3.region",
        ValueType: SettingValueType.String,
        DefaultValue: "\"fsn1\"",
        Category: "ObjectStorage",
        Description: "S3 region identifier (e.g., fsn1 for Hetzner, us-east-1 for AWS)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ForcePathStyle = new(
        Key: "s3.force_path_style",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "ObjectStorage",
        Description: "Use path-style URLs (required by most non-AWS S3 providers)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition UploadUrlExpirationMinutes = new(
        Key: "s3.upload_url_expiration_minutes",
        ValueType: SettingValueType.Integer,
        DefaultValue: "60",
        Category: "ObjectStorage",
        Description: "Presigned upload URL expiration time in minutes",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        Provider, DefaultMaxUploadBytes, DefaultTenantQuotaBytes, InstanceMaxUploadBytes, RouteMatrix,
        Endpoint, PublicEndpoint, BucketName,
        Region, ForcePathStyle, UploadUrlExpirationMinutes
    ];
}
