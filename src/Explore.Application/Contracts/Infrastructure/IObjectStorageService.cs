using System;
using System.Collections.Generic;
using System.Text;
namespace Explore.Application.Contracts.Infrastructure;

public interface IObjectStorageService
{
    /// <summary>
    /// Generates a pre-signed URL for downloading/viewing a file from S3-compatible storage.
    /// </summary>
    /// <param name="objectKey">The key of the object to retrieve.</param>
    /// <param name="safeDisplayName">The sanitized attachment filename.</param>
    /// <param name="expirationMinutes">URL expiration time in minutes (default: 60).</param>
    /// <returns>The presigned download URL.</returns>
    Task<string> GeneratePresignedDownloadUrl(
        string objectKey,
        string safeDisplayName,
        int expirationMinutes = 60);

    /// <summary>
    /// Retrieves a file stream from S3-compatible storage.
    /// </summary>
    /// <param name="fileKey">The key of the file to retrieve.</param>
    /// <returns>A tuple containing the file stream and content type.</returns>
    Task<(Stream FileStream, string ContentType)> GetFileStream(string fileKey);

    /// <summary>
    /// Tests connectivity to the configured S3-compatible storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection is successful, false otherwise.</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
