namespace Explore.Application.DTOs.StorageObject;

/// <summary>
/// Response DTO for pre-signed download URL generation
/// </summary>
public sealed record PresignedDownloadUrlResponseDto
{
    /// <summary>
    /// The pre-signed URL for downloading/viewing the file from S3
    /// </summary>
    public string PresignedUrl { get; init; } = string.Empty;

    /// <summary>
    /// The object key (path) of the file in storage
    /// </summary>
    public string ObjectKey { get; init; } = string.Empty;

    /// <summary>
    /// The expiration time of the pre-signed URL in minutes
    /// </summary>
    public int ExpiresInMinutes { get; init; }

    public string SafeDisplayName { get; init; } = "download";

    public bool ShouldDownloadAsAttachment { get; init; }
}
