namespace Explore.Application.Models;

/// <summary>
/// S3 connection parameters resolved from governance plus external credentials.
/// Instance admin can lock settings (IsLocked) to enforce a SaaS-wide storage provider,
/// or leave unlocked so tenants can bring their own S3-compatible storage.
/// </summary>
public class S3Configuration
{
    /// <summary>S3 endpoint URL (e.g., "https://fsn1.your-objectstorage.com").</summary>
    public required string Endpoint { get; set; }

    /// <summary>Bucket name for this tenant's storage.</summary>
    public required string BucketName { get; set; }

    /// <summary>S3 access key ID for authentication.</summary>
    public required string AccessKeyId { get; set; }

    /// <summary>S3 secret access key for authentication, resolved from external authority.</summary>
    public required string SecretAccessKey { get; set; }

    /// <summary>S3 region identifier (e.g., "fsn1" for Hetzner, "us-east-1" for AWS).</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Separate endpoint for presigned URLs, if different from the internal endpoint.
    /// Useful when the internal endpoint (e.g., Docker network) differs from the public-facing one.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    /// <summary>
    /// Use path-style URLs (https://endpoint/bucket/key) instead of virtual-hosted
    /// (https://bucket.endpoint/key). Required by most non-AWS S3-compatible providers.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Presigned upload URL expiration in minutes.</summary>
    public int UploadUrlExpirationMinutes { get; set; } = 60;

    /// <summary>Presigned download URL expiration in minutes.</summary>
    public int DownloadUrlExpirationMinutes { get; set; } = 60;
}
