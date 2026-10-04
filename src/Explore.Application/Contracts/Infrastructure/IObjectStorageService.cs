using System;
using System.Collections.Generic;
using System.Text;
namespace Explore.Application.Contracts.Infrastructure;

public interface IObjectStorageService
{
    /// <summary>
    /// Generates a pre-signed URL for downloading/viewing a file from S3-compatible storage.
    /// </summary>
    /// <param name="bindingId">The object's captured storage target, not current configuration.</param>
    /// <param name="objectKey">The key of the object to retrieve.</param>
    /// <param name="safeDisplayName">The sanitized attachment filename.</param>
    /// <param name="expirationMinutes">URL expiration time in minutes (default: 60).</param>
    /// <param name="providerVersionId">The captured object version; required for a versioned bucket.</param>
    /// <returns>The presigned download URL.</returns>
    Task<string> GeneratePresignedDownloadUrl(
        Guid bindingId,
        string objectKey,
        string safeDisplayName,
        int expirationMinutes = 60,
        string? providerVersionId = null);

    /// <summary>
    /// Retrieves a file stream from S3-compatible storage.
    /// </summary>
    /// <param name="bindingId">The object's captured storage target.</param>
    /// <param name="fileKey">The key of the file to retrieve.</param>
    /// <param name="providerVersionId">The captured object version; required for a versioned bucket.</param>
    /// <returns>A tuple containing the file stream and content type.</returns>
    Task<(Stream FileStream, string ContentType)> GetFileStream(Guid bindingId, string fileKey, string? providerVersionId = null);

    /// <summary>
    /// Tests connectivity to the configured S3-compatible storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection is successful, false otherwise.</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
