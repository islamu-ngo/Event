using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Contracts.Infrastructure;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Services;

public class ObjectStorageService : IObjectStorageService
{
    private readonly IS3ConfigResolver _configResolver;
    private readonly IS3ClientFactory _clientFactory;
    private readonly ILogger<ObjectStorageService> _logger;

    public ObjectStorageService(
        IS3ConfigResolver configResolver,
        IS3ClientFactory clientFactory,
        ILogger<ObjectStorageService> logger)
    {
        _configResolver = configResolver;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<(Stream FileStream, string ContentType)> GetFileStream(string fileKey)
    {
        if (string.IsNullOrWhiteSpace(fileKey))
            throw new ArgumentException("fileKey must be provided", nameof(fileKey));

        var config = await _configResolver.ResolveAsync();
        if (config is null)
            throw new InvalidOperationException("S3 storage is not configured.");

        var client = _clientFactory.CreateDataClient(config);

        try
        {
            var request = new GetObjectRequest
            {
                BucketName = config.BucketName,
                Key = fileKey
            };

            var response = await client.GetObjectAsync(request);
            return (response.ResponseStream, response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException($"S3 object not found. Key: {fileKey}", ex);
        }
    }

    Task<(Stream FileStream, string ContentType)> IObjectStorageService.GetFileStream(string fileKey)
        => GetFileStream(fileKey);

    public async Task<string> GeneratePresignedDownloadUrl(
        string objectKey,
        string safeDisplayName,
        int expirationMinutes = 60)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("objectKey must be provided", nameof(objectKey));

        var config = await _configResolver.ResolveAsync();
        if (config is null)
            throw new InvalidOperationException("S3 storage is not configured.");

        var presignClient = _clientFactory.CreatePresignClient(config);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = config.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = BuildAttachmentContentDisposition(safeDisplayName)
            }
        };

        string? downloadUrl;
        try
        {
            downloadUrl = presignClient.GetPreSignedURL(request);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to generate pre-signed download URL. Verify S3 configuration and credentials.", ex);
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("Failed to generate pre-signed download URL: the S3 client returned an empty URL.");
        }

        return downloadUrl;
    }

    private static string BuildAttachmentContentDisposition(string safeDisplayName)
    {
        var disposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = IsSafeDisplayName(safeDisplayName) ? safeDisplayName : "download"
        };
        return disposition.ToString();
    }

    private static bool IsSafeDisplayName(string value)
        => value.Length is > 0 and <= 255
            && value is not "." and not ".."
            && !value.Any(char.IsControl)
            && !value.Contains('/', StringComparison.Ordinal)
            && !value.Contains('\\', StringComparison.Ordinal);

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configResolver.ResolveAsync(cancellationToken);
        if (config is null)
            return false;

        try
        {
            var client = _clientFactory.CreateDataClient(config);
            await client.ListBucketsAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "S3 connection test failed. FailureType={FailureType}",
                CategorizeConnectionFailure(ex));
            return false;
        }
    }

    private static string CategorizeConnectionFailure(Exception exception) =>
        exception switch
        {
            AmazonS3Exception => "s3_service_error",
            AmazonServiceException => "provider_service_error",
            TimeoutException => "timeout",
            OperationCanceledException => "operation_canceled",
            IOException => "provider_io",
            InvalidOperationException => "provider_unavailable",
            _ => "unknown"
        };
}
